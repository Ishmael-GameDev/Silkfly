using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Tests.Thunderstore;

public sealed class ThunderstoreClientTests
{
    private const string IndexUrl = "https://thunderstore.io/c/hollow-knight-silksong/api/v1/package-listing-index/";
    private const string ListUrl = "https://thunderstore.io/c/hollow-knight-silksong/api/v1/package/";
    private const string ChunkUrl = "https://ccdn.thunderstore.io/live/blob-storage/chunk1.blob";

    [Fact]
    public async Task Listing_index_strategy_reads_gzip_chunks()
    {
        var handler = new FakeHandler
        {
            [IndexUrl] = _ => Gzip($"[\"{ChunkUrl}\"]"),
            [ChunkUrl] = _ => Gzip(Fixture.Read("thunderstore", "package-listing.json")),
        };

        var packages = await CreateClient(handler).FetchCommunityPackagesAsync("hollow-knight-silksong", null, default);

        packages.Count.ShouldBe(Fixture.ListingDtos().Count);
        handler.Requests.ShouldNotContain(ListUrl);
    }

    [Fact]
    public async Task Falls_back_to_v1_package_when_index_fails()
    {
        var handler = new FakeHandler
        {
            [IndexUrl] = _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            [ListUrl] = _ => Json(Fixture.Read("thunderstore", "package-listing.json")),
        };

        var packages = await CreateClient(handler).FetchCommunityPackagesAsync("hollow-knight-silksong", null, default);

        packages.Count.ShouldBe(Fixture.ListingDtos().Count);
    }

    [Fact]
    public async Task Bad_json_is_a_format_error_when_both_strategies_fail()
    {
        var handler = new FakeHandler
        {
            [IndexUrl] = _ => Json("not json"),
            [ListUrl] = _ => Json("{ also not a list"),
        };

        await Should.ThrowAsync<ThunderstoreFormatException>(
            () => CreateClient(handler).FetchCommunityPackagesAsync("hollow-knight-silksong", null, default));
    }

    [Fact]
    public async Task Readme_is_returned_and_null_markdown_means_none()
    {
        var handler = new FakeHandler
        {
            ["https://thunderstore.io/api/experimental/package/silksong_modding/ModMenu/0.8.0/readme/"] =
                _ => Json(Fixture.Read("thunderstore", "readme.json")),
            ["https://thunderstore.io/api/experimental/package/silksong_modding/ModMenu/0.8.0/changelog/"] =
                _ => Json(Fixture.Read("thunderstore", "changelog-null.json")),
        };
        var client = CreateClient(handler);
        var id = new PackageId("silksong_modding", "ModMenu");

        (await client.GetReadmeAsync(id, Versions.Parse("0.8.0"), default)).ShouldStartWith("# Silksong.ModMenu");
        (await client.GetChangelogAsync(id, Versions.Parse("0.8.0"), default)).ShouldBeNull();
    }

    [Fact]
    public async Task Download_rejects_untrusted_hosts()
    {
        var client = CreateClient(new FakeHandler());

        var ex = await Should.ThrowAsync<NetworkException>(
            () => client.DownloadAsync(new Uri("https://evil.example.com/a.zip"), new MemoryStream(), 0, null, default));
        ex.MessageKey.ShouldBe("Error_UntrustedHost");
    }

    [Fact]
    public async Task Download_verifies_size()
    {
        const string url = "https://ccdn.thunderstore.io/live/repository/packages/A-B-1.0.0.zip";
        var handler = new FakeHandler { [url] = _ => new HttpResponseMessage { Content = new ByteArrayContent(new byte[10]) } };
        var client = CreateClient(handler);

        var ok = new MemoryStream();
        await client.DownloadAsync(new Uri(url), ok, 10, null, default);
        ok.Length.ShouldBe(10);

        var ex = await Should.ThrowAsync<NetworkException>(
            () => client.DownloadAsync(new Uri(url), new MemoryStream(), 11, null, default));
        ex.MessageKey.ShouldBe("Error_DownloadSizeMismatch");
    }

    private static ThunderstoreClient CreateClient(FakeHandler handler) =>
        new(new FakeFactory(handler), NullLogger<ThunderstoreClient>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new() { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Gzip(string body)
    {
        var buffer = new MemoryStream();
        using (var gz = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(body));
        return new HttpResponseMessage { Content = new ByteArrayContent(buffer.ToArray()) };
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://thunderstore.io/") };
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

        public List<string> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> this[string url]
        {
            set => _routes[url] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            var response = _routes.TryGetValue(url, out var route) ? route(request) : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
