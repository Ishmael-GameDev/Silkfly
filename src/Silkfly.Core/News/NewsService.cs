using System.Globalization;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;

namespace Silkfly.Core.News;

public sealed record NewsItem(string Id, IReadOnlyDictionary<string, string> Text, Uri? Link, DateOnly? Until)
{
    public string TextFor(CultureInfo culture)
    {
        if (Text.TryGetValue(culture.TwoLetterISOLanguageName, out var text))
            return text;
        if (Text.TryGetValue("en", out text))
            return text;
        return Text.Values.First();
    }
}

public interface INewsService
{
    Task<NewsItem?> GetLatestAsync(CancellationToken ct);
}

public sealed record NewsOptions(Uri FeedUrl);

public sealed class NewsService(
    IHttpClientFactory httpFactory,
    IFileSystem fs,
    IAppPaths paths,
    NewsOptions options,
    TimeProvider time,
    ILogger<NewsService> logger) : INewsService
{
    public const string HttpClientName = "news";

    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private const int MaxFeedBytes = 256 * 1024;

    private string CacheFile => fs.Path.Combine(paths.CacheRoot, "news.json");

    public async Task<NewsItem?> GetLatestAsync(CancellationToken ct)
    {
        var json = await ReadFeedAsync(ct).ConfigureAwait(false);
        if (json is null)
            return null;

        try
        {
            var feed = JsonSerializer.Deserialize(json, NewsJsonContext.Default.NewsFeedDto);
            return Pick(feed, DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "news.json is malformed");
            return null;
        }
    }

    private async Task<string?> ReadFeedAsync(CancellationToken ct)
    {
        var cached = fs.File.Exists(CacheFile) ? fs.File.GetLastWriteTimeUtc(CacheFile) : (DateTime?)null;
        if (cached is { } written && time.GetUtcNow().UtcDateTime - written < CacheLifetime)
            return await fs.File.ReadAllTextAsync(CacheFile, ct).ConfigureAwait(false);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            using var client = httpFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(options.FeedUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxFeedBytes)
                throw new HttpRequestException("news.json is too large");

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (json.Length > MaxFeedBytes)
                throw new HttpRequestException("news.json is too large");

            await AtomicFile.WriteAllTextAsync(fs, CacheFile, json, ct: ct).ConfigureAwait(false);
            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogInformation("News feed unavailable: {Error}", ex.Message);
            return cached is null ? null : await fs.File.ReadAllTextAsync(CacheFile, ct).ConfigureAwait(false);
        }
    }

    internal static NewsItem? Pick(NewsFeedDto? feed, DateOnly today)
    {
        foreach (var dto in feed?.Items ?? [])
        {
            var text = dto.Text?
                .Where(t => !string.IsNullOrWhiteSpace(t.Value))
                .ToDictionary(t => t.Key.ToLowerInvariant(), t => t.Value.Trim());
            if (text is not { Count: > 0 })
                continue;

            DateOnly? until = DateOnly.TryParseExact(dto.Until, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var u)
                ? u
                : null;
            if (until < today)
                continue;

            var link = Uri.TryCreate(dto.Link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
            return new NewsItem(dto.Id ?? string.Empty, text, link, until);
        }

        return null;
    }
}

internal sealed class NewsFeedDto
{
    public List<NewsItemDto>? Items { get; set; }
}

internal sealed class NewsItemDto
{
    public string? Id { get; set; }
    public Dictionary<string, string>? Text { get; set; }
    public string? Link { get; set; }
    public string? Until { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(NewsFeedDto))]
internal sealed partial class NewsJsonContext : JsonSerializerContext;
