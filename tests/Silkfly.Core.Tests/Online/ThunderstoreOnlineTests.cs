using System.IO.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Catalog;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Loader;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Tests.Loader;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Tests.Online;

public sealed class OnlineFactAttribute : FactAttribute
{
    public OnlineFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SILKFLY_ONLINE_TESTS") != "1")
            Skip = "Online test: set SILKFLY_ONLINE_TESTS=1 to run it.";
    }
}

public sealed class ThunderstoreOnlineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "silkfly-online-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ThunderstoreClient CreateClient() => new(new RealHttpFactory(), NullLogger<ThunderstoreClient>.Instance);

    private static async Task<IReadOnlyList<Package>> LoadCatalogAsync(ThunderstoreClient client)
    {
        var dtos = await client.FetchCommunityPackagesAsync(CatalogRules.Community, null, CancellationToken.None);
        return CatalogMapper.Map(dtos, NullLogger.Instance);
    }

    [OnlineFact]
    public async Task Real_catalog_loads_and_has_the_loader()
    {
        var packages = await LoadCatalogAsync(CreateClient());

        packages.Count.ShouldBeGreaterThan(0);
        packages.ShouldContain(p => p.Id == CatalogRules.LoaderPackage);
    }

    [OnlineFact]
    public async Task Small_package_downloads_and_installs_into_a_temporary_profile()
    {
        var client = CreateClient();
        var packages = await LoadCatalogAsync(client);

        var version = packages
            .Where(p => !CatalogRules.IsHidden(p.Id) && !p.IsDeprecated)
            .Select(p => p.Latest)
            .Where(v => v.IsActive && v.FileSize > 0 && v.Dependencies.All(d => d.Id.IsLoaderPack()))
            .MinBy(v => v.FileSize);
        version.ShouldNotBeNull();

        var fs = new FileSystem();
        var settings = new SettingsService(fs, new AppPaths(_root), TimeProvider.System, NullLogger<SettingsService>.Instance);
        settings.Load();
        var paths = new AppPaths(_root);
        var cache = new PackageCache(fs, paths, client, TimeProvider.System, NullLogger<PackageCache>.Instance);
        var locks = new ProfileLockStore(fs, TimeProvider.System, NullLogger<ProfileLockStore>.Instance);
        var loader = new LoaderService(fs, paths, new LoaderServiceTests.FakeCatalog(), cache, TimeProvider.System, NullLogger<LoaderService>.Instance);
        var profiles = new ProfileService(fs, paths, locks, loader, settings, TimeProvider.System, NullLogger<ProfileService>.Instance);
        await profiles.LoadAsync(CancellationToken.None);
        var profile = await profiles.CreateAsync("Online", CancellationToken.None);
        var installer = new ModInstaller(fs, profiles, new PackageLayout(), TimeProvider.System, NullLogger<ModInstaller>.Instance);

        var zip = await cache.GetOrDownloadAsync(version, null, CancellationToken.None);
        var mod = await installer.InstallArchiveAsync(
            profile, zip, version.PackageId, version.Version, ModSource.Thunderstore, isExplicit: true, CancellationToken.None);

        mod.Files.ShouldNotBeEmpty();
        profiles.GetLock(profile).Find(version.PackageId).ShouldNotBeNull();
        mod.Files.ShouldAllBe(f => File.Exists(Path.Combine(profile.RootPath, f.Replace('/', Path.DirectorySeparatorChar))));
    }

    private sealed class RealHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
            {
                BaseAddress = new Uri("https://thunderstore.io/"),
                Timeout = Timeout.InfiniteTimeSpan,
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Silkfly-tests", "1.0"));
            return client;
        }
    }
}
