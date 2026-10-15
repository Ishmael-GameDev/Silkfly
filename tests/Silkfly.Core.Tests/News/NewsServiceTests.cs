using System.Globalization;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.News;

namespace Silkfly.Core.Tests.News;

public sealed class NewsServiceTests
{
    private const string FeedUrl = "https://raw.githubusercontent.com/owner/Silkfly/main/news.json";

    private const string Feed = """
        { "items": [
            { "id": "old", "text": { "en": "Expired" }, "until": "2026-10-01" },
            { "id": "empty", "text": { "en": " " } },
            { "id": "release", "text": { "ru": "Вышла 0.1", "en": "0.1 is out" },
              "link": "https://github.com/owner/Silkfly/releases", "until": "2026-11-14" },
            { "id": "later", "text": { "en": "Not shown, an earlier item wins" } }
        ] }
        """;

    private readonly MockFileSystem _fs = new();
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "silkfly-news-tests"));
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeHandler _handler = new();

    private string CacheFile => Path.Combine(_paths.CacheRoot, "news.json");

    private NewsService CreateService() => new(
        new FakeFactory(_handler), _fs, _paths, new NewsOptions(new Uri(FeedUrl)), _time, NullLogger<NewsService>.Instance);

    [Fact]
    public async Task Picks_first_item_that_has_text_and_has_not_expired()
    {
        _handler.Respond = () => Json(Feed);

        var item = await CreateService().GetLatestAsync(default);

        item.ShouldNotBeNull();
        item.Id.ShouldBe("release");
        item.Link.ShouldBe(new Uri("https://github.com/owner/Silkfly/releases"));
        item.Until.ShouldBe(new DateOnly(2026, 11, 14));
        item.TextFor(CultureInfo.GetCultureInfo("ru-RU")).ShouldBe("Вышла 0.1");
        item.TextFor(CultureInfo.GetCultureInfo("de-DE")).ShouldBe("0.1 is out");
    }

    [Fact]
    public async Task Item_is_shown_through_its_last_day()
    {
        _time.Now = new DateTimeOffset(2026, 11, 14, 23, 0, 0, TimeSpan.Zero);
        _handler.Respond = () => Json(Feed);

        (await CreateService().GetLatestAsync(default))!.Id.ShouldBe("release");
    }

    [Fact]
    public void Non_https_links_are_dropped()
    {
        var feed = new NewsFeedDto
        {
            Items = [new NewsItemDto { Id = "x", Text = new() { ["en"] = "Hi" }, Link = "file:///C:/Windows/System32/calc.exe" }],
        };

        NewsService.Pick(feed, new DateOnly(2026, 10, 14))!.Link.ShouldBeNull();
    }

    [Fact]
    public async Task Fresh_cache_is_used_without_a_request()
    {
        _fs.AddFile(CacheFile, new MockFileData(Feed));
        _fs.File.SetLastWriteTimeUtc(CacheFile, _time.Now.UtcDateTime.AddMinutes(-30));

        (await CreateService().GetLatestAsync(default))!.Id.ShouldBe("release");
        _handler.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task Stale_cache_is_refreshed_and_rewritten()
    {
        _fs.AddFile(CacheFile, new MockFileData("""{ "items": [] }"""));
        _fs.File.SetLastWriteTimeUtc(CacheFile, _time.Now.UtcDateTime.AddHours(-2));
        _handler.Respond = () => Json(Feed);

        (await CreateService().GetLatestAsync(default))!.Id.ShouldBe("release");
        _handler.Requests.ShouldBe(1);
        _fs.File.ReadAllText(CacheFile).ShouldBe(Feed);
    }

    [Fact]
    public async Task Offline_falls_back_to_stale_cache()
    {
        _fs.AddFile(CacheFile, new MockFileData(Feed));
        _fs.File.SetLastWriteTimeUtc(CacheFile, _time.Now.UtcDateTime.AddDays(-3));
        _handler.Respond = () => throw new HttpRequestException("offline");

        (await CreateService().GetLatestAsync(default))!.Id.ShouldBe("release");
    }

    [Fact]
    public async Task Offline_without_cache_and_bad_json_mean_no_news()
    {
        _handler.Respond = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        (await CreateService().GetLatestAsync(default)).ShouldBeNull();

        _handler.Respond = () => Json("{ not json");
        (await CreateService().GetLatestAsync(default)).ShouldBeNull();
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            request.RequestUri!.AbsoluteUri.ShouldBe(FeedUrl);
            return Task.FromResult(Respond());
        }
    }
}
