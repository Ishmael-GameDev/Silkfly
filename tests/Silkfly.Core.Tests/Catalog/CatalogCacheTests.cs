using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Catalog;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;

namespace Silkfly.Core.Tests.Catalog;

public sealed class CatalogCacheTests
{
    private readonly MockFileSystem _fs = new();
    private readonly AppPaths _paths = new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "silkfly-cache-tests"));

    private CatalogCache CreateCache() => new(_fs, _paths, NullLogger<CatalogCache>.Instance);

    [Fact]
    public async Task Missing_cache_returns_null() =>
        (await CreateCache().LoadAsync(default)).ShouldBeNull();

    [Fact]
    public async Task Round_trip_preserves_packages()
    {
        var fetchedAt = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero);
        var original = CatalogSnapshot.Create(Fixture.ListingPackages(), fetchedAt, CatalogSource.Network);

        await CreateCache().SaveAsync(original, default);
        var loaded = await CreateCache().LoadAsync(default);

        loaded.ShouldNotBeNull();
        loaded.Source.ShouldBe(CatalogSource.Cache);
        loaded.FetchedAt.ShouldBe(fetchedAt);
        loaded.Packages.Count.ShouldBe(original.Packages.Count);

        foreach (var expected in original.Packages)
        {
            var actual = loaded.ById[expected.Id];
            actual.Owner.ShouldBe(expected.Owner);
            actual.Categories.ShouldBe(expected.Categories);
            actual.IsDeprecated.ShouldBe(expected.IsDeprecated);
            actual.Versions.Select(v => v.Version.ToString()).ShouldBe(expected.Versions.Select(v => v.Version.ToString()));
            actual.Latest.Dependencies.Select(d => d.ToString()).ShouldBe(expected.Latest.Dependencies.Select(d => d.ToString()));
            actual.Latest.DownloadUrl.ShouldBe(expected.Latest.DownloadUrl);
        }
    }

    [Fact]
    public async Task Corrupt_cache_is_ignored()
    {
        _fs.AddFile(_fs.Path.Combine(_paths.CacheRoot, "catalog.json"), new MockFileData("{ broken"));
        _fs.AddFile(_fs.Path.Combine(_paths.CacheRoot, "catalog.meta.json"), new MockFileData("""{"schema":1,"fetchedAt":"2026-10-14T00:00:00Z"}"""));

        (await CreateCache().LoadAsync(default)).ShouldBeNull();
    }
}
