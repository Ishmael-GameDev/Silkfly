using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Semver;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;

namespace Silkfly.Core.Thunderstore;

public sealed class ThunderstoreClient(
    IHttpClientFactory httpFactory,
    ILogger<ThunderstoreClient> logger) : IThunderstoreClient
{
    public const string HttpClientName = "thunderstore";

    private const int MaxAttempts = 3;
    private const int MaxParallelChunks = 4;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<PackageDto>> FetchCommunityPackagesAsync(
        string community, IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            return await FetchViaListingIndexAsync(community, progress, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ThunderstoreUnavailableException or ThunderstoreFormatException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "package-listing-index failed, falling back to api/v1/package/");
        }

        var packages = await WithRetriesAsync(
            async token =>
            {
                using var response = await SendAsync($"c/{community}/api/v1/package/", token).ConfigureAwait(false);
                await using var stream = await OpenBodyAsync(response, token).ConfigureAwait(false);
                return await DeserializeAsync(stream, ThunderstoreJsonContext.Default.ListPackageDto, token).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);

        progress?.Report(1);
        return packages;
    }

    private async Task<IReadOnlyList<PackageDto>> FetchViaListingIndexAsync(
        string community, IProgress<double>? progress, CancellationToken ct)
    {
        var chunkUrls = await WithRetriesAsync(
            async token =>
            {
                using var response = await SendAsync($"c/{community}/api/v1/package-listing-index/", token).ConfigureAwait(false);
                await using var stream = await OpenBodyAsync(response, token).ConfigureAwait(false);
                return await DeserializeAsync(stream, ThunderstoreJsonContext.Default.ListString, token).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);

        var results = new List<PackageDto>[chunkUrls.Count];
        var done = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, chunkUrls.Count),
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelChunks, CancellationToken = ct },
            async (i, token) =>
            {
                results[i] = await WithRetriesAsync(
                    async t =>
                    {
                        using var response = await SendAsync(chunkUrls[i], t).ConfigureAwait(false);
                        await using var stream = await OpenBodyAsync(response, t).ConfigureAwait(false);
                        return await DeserializeAsync(stream, ThunderstoreJsonContext.Default.ListPackageDto, t).ConfigureAwait(false);
                    },
                    token).ConfigureAwait(false);
                progress?.Report((double)Interlocked.Increment(ref done) / chunkUrls.Count);
            }).ConfigureAwait(false);

        return results.SelectMany(r => r).ToList();
    }

    public Task<string?> GetReadmeAsync(PackageId id, SemVersion version, CancellationToken ct) =>
        GetMarkdownAsync(id, version, "readme", ct);

    public Task<string?> GetChangelogAsync(PackageId id, SemVersion version, CancellationToken ct) =>
        GetMarkdownAsync(id, version, "changelog", ct);

    private Task<string?> GetMarkdownAsync(PackageId id, SemVersion version, string kind, CancellationToken ct) =>
        WithRetriesAsync(
            async token =>
            {
                var path = $"api/experimental/package/{Uri.EscapeDataString(id.Namespace)}/{Uri.EscapeDataString(id.Name)}/{version}/{kind}/";
                using var response = await SendAsync(path, token, allowNotFound: true).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                await using var stream = await OpenBodyAsync(response, token).ConfigureAwait(false);
                var dto = await DeserializeAsync(stream, ThunderstoreJsonContext.Default.MarkdownDto, token).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(dto.Markdown) ? null : dto.Markdown;
            },
            ct);

    private const string ProfileCodePrefix = "#r2modman";

    public async Task<string> CreateProfileCodeAsync(byte[] r2zBytes, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(HttpClientName);
        var body = $"{ProfileCodePrefix}\n{Convert.ToBase64String(r2zBytes)}";
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/octet-stream");
        try
        {
            using var response = await client.PostAsync("api/experimental/legacyprofile/create/", content, ct).ConfigureAwait(false);
            ThrowOnFailure(response);
            await using var stream = await OpenBodyAsync(response, ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("key", out var key) && key.GetString() is { Length: > 0 } code
                ? code
                : throw new ThunderstoreFormatException();
        }
        catch (HttpRequestException ex)
        {
            throw new ThunderstoreUnavailableException(ex);
        }
        catch (JsonException ex)
        {
            throw new ThunderstoreFormatException(ex);
        }
    }

    public Task<byte[]> GetProfileByCodeAsync(string code, CancellationToken ct) =>
        WithRetriesAsync(
            async token =>
            {
                if (!Guid.TryParse(code, out _))
                    throw new SilkflyException("Error_BadProfileCode");

                using var response = await SendAsync($"api/experimental/legacyprofile/get/{code}/", token, allowNotFound: true).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new SilkflyException("Error_ProfileCodeNotFound", code);

                var text = (await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Trim();
                if (text.StartsWith(ProfileCodePrefix, StringComparison.Ordinal))
                    text = text[ProfileCodePrefix.Length..].Trim();
                try
                {
                    return Convert.FromBase64String(text);
                }
                catch (FormatException ex)
                {
                    throw new ThunderstoreFormatException(ex);
                }
            },
            ct);

    public async Task DownloadAsync(
        Uri downloadUrl, Stream destination, long expectedSize, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        EnsureTrustedHost(downloadUrl);

        var client = httpFactory.CreateClient(HttpClientName);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(DownloadIdleTimeout);

        try
        {
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, idle.Token)
                .ConfigureAwait(false);
            EnsureTrustedHost(response.RequestMessage?.RequestUri ?? downloadUrl);
            ThrowOnFailure(response);

            await using var body = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, idle.Token).ConfigureAwait(false)) > 0)
            {
                idle.CancelAfter(DownloadIdleTimeout);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                total += read;
                bytesProgress?.Report(total);
            }

            if (expectedSize > 0 && total != expectedSize)
                throw new NetworkException("Error_DownloadSizeMismatch", null, downloadUrl, expectedSize, total);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ThunderstoreUnavailableException(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ThunderstoreUnavailableException(ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string pathOrUrl, CancellationToken ct, bool allowNotFound = false)
    {
        var client = httpFactory.CreateClient(HttpClientName);
        var uri = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute) ? absolute : new Uri(client.BaseAddress!, pathOrUrl);
        EnsureTrustedHost(uri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ThunderstoreUnavailableException(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ThunderstoreUnavailableException(ex);
        }

        EnsureTrustedHost(response.RequestMessage?.RequestUri ?? uri);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return response;

        try
        {
            ThrowOnFailure(response);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        return response;
    }

    private static void ThrowOnFailure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var status = (int)response.StatusCode;
        if (status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ThunderstoreUnavailableException(new HttpRequestException($"HTTP {status}", null, response.StatusCode));

        throw new NetworkException("Error_HttpStatus", new HttpRequestException($"HTTP {status}", null, response.StatusCode), status);
    }

    private static async Task<Stream> OpenBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var memory = new MemoryStream(bytes, writable: false);
        return bytes is [0x1f, 0x8b, ..]
            ? new GZipStream(memory, CompressionMode.Decompress)
            : memory;
    }

    private static async Task<T> DeserializeAsync<T>(
        Stream stream, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false)
                   ?? throw new ThunderstoreFormatException();
        }
        catch (JsonException ex)
        {
            throw new ThunderstoreFormatException(ex);
        }
        catch (InvalidDataException ex)
        {
            throw new ThunderstoreFormatException(ex);
        }
    }

    private async Task<T> WithRetriesAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(ct).ConfigureAwait(false);
            }
            catch (ThunderstoreUnavailableException ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                logger.LogInformation(ex, "Thunderstore request failed (attempt {Attempt}), retrying in {Delay}", attempt, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    public static void EnsureTrustedHost(Uri uri)
    {
        var host = uri.Host;
        var trusted = uri.Scheme == Uri.UriSchemeHttps &&
                      (host.Equals("thunderstore.io", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase));
        if (!trusted)
            throw new NetworkException("Error_UntrustedHost", null, uri.Host);
    }
}
