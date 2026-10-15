using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Semver;
using Silkfly.Core.Catalog;
using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Loader;
using Silkfly.Core.Models;

namespace Silkfly.Core.Tests.Loader;

public sealed class LoaderServiceTests
{
    private static readonly string GameDir = TestPath.Of(@"C:\Games\Hollow Knight Silksong");
    private readonly MockFileSystem _fs = new();
    private readonly AppPaths _paths = new(TestPath.Of(@"C:\Silkfly"));
    private readonly FakePackageCache _cache;
    private readonly FakeCatalog _catalog = new();
    private readonly GameInstall _game = new(GameDir, GameSource.Gog, GamePlatform.Windows,
        $@"{GameDir}\Hollow Knight Silksong.exe", $@"{GameDir}\Hollow Knight Silksong_Data");

    public LoaderServiceTests()
    {
        _cache = new FakePackageCache(_fs);
        _fs.AddDirectory(GameDir);
        _catalog.Add(TestCatalog.Package("silksong_modding-BepInExPack_Silksong", "1.0.3"));
        _cache.Add(CatalogRules.LoaderPackage, "1.0.3", PackEntries("1.0.3"));
    }

    private LoaderService CreateService() =>
        new(_fs, _paths, _catalog, _cache, TimeProvider.System, NullLogger<LoaderService>.Instance);

    [Fact]
    public async Task Install_places_windows_files_and_disables_doorstop()
    {
        await CreateService().InstallOrUpdateAsync(_game, null, default);

        foreach (var file in LoaderService.FilesFor(GamePlatform.Windows))
            _fs.File.Exists(_fs.Path.Combine(GameDir, file)).ShouldBeTrue(file);
        _fs.File.Exists(_fs.Path.Combine(GameDir, "run_bepinex.sh")).ShouldBeFalse();
        _fs.Directory.Exists(_fs.Path.Combine(GameDir, "BepInEx")).ShouldBeFalse();
        DoorstopIni.Get(_fs.File.ReadAllText(_fs.Path.Combine(GameDir, "doorstop_config.ini")), "General", "enabled").ShouldBe("false");

        var status = await CreateService().GetStatusAsync(_game, default);
        status.IsInstalled.ShouldBeTrue();
        status.InstalledPackVersion!.ToString().ShouldBe("1.0.3");
        status.UpdateAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Foreign_files_are_backed_up_and_restored_on_remove()
    {
        _fs.AddFile(_fs.Path.Combine(GameDir, "winhttp.dll"), new MockFileData("other manager's proxy"));
        _fs.AddFile(_fs.Path.Combine(GameDir, "steam_appid.txt"), new MockFileData("1030300"));

        (await CreateService().GetStatusAsync(_game, default)).ForeignLoaderDetected.ShouldBeTrue();
        await CreateService().InstallOrUpdateAsync(_game, null, default);

        _fs.File.ReadAllText(_fs.Path.Combine(GameDir, ".silkfly-backup", "winhttp.dll")).ShouldBe("other manager's proxy");
        _fs.File.ReadAllText(_fs.Path.Combine(GameDir, "winhttp.dll")).ShouldBe("proxy 1.0.3");

        await CreateService().RemoveAsync(_game, default);

        _fs.File.ReadAllText(_fs.Path.Combine(GameDir, "winhttp.dll")).ShouldBe("other manager's proxy");
        _fs.File.Exists(_fs.Path.Combine(GameDir, "doorstop_config.ini")).ShouldBeFalse();
        _fs.File.Exists(_fs.Path.Combine(GameDir, LoaderService.ManifestFileName)).ShouldBeFalse();
        _fs.File.ReadAllText(_fs.Path.Combine(GameDir, "steam_appid.txt")).ShouldBe("1030300");
    }

    [Fact]
    public async Task Update_refreshes_core_in_every_profile_and_reports_versions()
    {
        var profile = _fs.Path.Combine(_paths.ProfilesRoot, Guid.NewGuid().ToString());
        _fs.AddFile(_fs.Path.Combine(profile, "silkfly-profile.json"), new MockFileData("{}"));
        await CreateService().InstallOrUpdateAsync(_game, null, default);
        _fs.File.ReadAllText(_fs.Path.Combine(profile, "BepInEx", "core", "BepInEx.Preloader.dll")).ShouldBe("preloader 1.0.3");

        _catalog.Add(TestCatalog.Package("silksong_modding-BepInExPack_Silksong", ["1.0.3", "1.0.4"]));
        _cache.Add(CatalogRules.LoaderPackage, "1.0.4", PackEntries("1.0.4"));
        (await CreateService().GetStatusAsync(_game, default)).UpdateAvailable.ShouldBeTrue();

        await CreateService().InstallOrUpdateAsync(_game, null, default);

        _fs.File.ReadAllText(_fs.Path.Combine(profile, "BepInEx", "core", "BepInEx.Preloader.dll")).ShouldBe("preloader 1.0.4");
        _fs.File.ReadAllText(_fs.Path.Combine(GameDir, "winhttp.dll")).ShouldBe("proxy 1.0.4");
        _fs.Directory.Exists(_fs.Path.Combine(GameDir, ".silkfly-backup")).ShouldBeFalse();
    }

    [Fact]
    public async Task Profile_core_keeps_existing_config()
    {
        await CreateService().InstallOrUpdateAsync(_game, null, default);
        var profile = TestPath.Of(@"C:\Silkfly\profiles\p1");
        _fs.AddFile(_fs.Path.Combine(profile, "BepInEx", "config", "BepInEx.cfg"), new MockFileData("user edited"));

        (await CreateService().EnsureProfileCoreAsync(profile, default)).ShouldBeTrue();

        _fs.File.ReadAllText(_fs.Path.Combine(profile, "BepInEx", "config", "BepInEx.cfg")).ShouldBe("user edited");
        _fs.Directory.Exists(_fs.Path.Combine(profile, "BepInEx", "plugins")).ShouldBeTrue();
    }

    [Fact]
    public async Task Profile_core_needs_an_unpacked_loader() =>
        (await CreateService().EnsureProfileCoreAsync(TestPath.Of(@"C:\Silkfly\profiles\p1"), default)).ShouldBeFalse();

    private static Dictionary<string, string> PackEntries(string version) => new()
    {
        ["manifest.json"] = "{}",
        ["BepInExPack/"] = "",
        ["BepInExPack/winhttp.dll"] = $"proxy {version}",
        ["BepInExPack/doorstop_config.ini"] = "[General]\nenabled = true\ntarget_assembly=BepInEx\\core\\BepInEx.Preloader.dll\n",
        ["BepInExPack/.doorstop_version"] = "4.4.1",
        ["BepInExPack/steam_appid.txt"] = "1030300",
        ["BepInExPack/run_bepinex.sh"] = "#!/bin/sh",
        ["BepInExPack/libdoorstop.so"] = "elf",
        ["BepInExPack/BepInEx/core/BepInEx.Preloader.dll"] = $"preloader {version}",
        ["BepInExPack/BepInEx/core/BepInEx.dll"] = "bepinex",
        ["BepInExPack/BepInEx/config/BepInEx.cfg"] = "[Logging]",
    };

    internal sealed class FakeCatalog : ICatalogService
    {
        private readonly Dictionary<PackageId, Package> _packages = new();

        public void Add(Package p) => _packages[p.Id] = p;

        public CatalogSnapshot Current => CatalogSnapshot.Create(_packages.Values, DateTimeOffset.UtcNow, CatalogSource.Network);
        public bool IsRefreshing => false;
        public Exception? LastRefreshError => null;
        public event EventHandler<CatalogSnapshot>? CatalogChanged { add { } remove { } }
        public event EventHandler? RefreshStateChanged { add { } remove { } }
        public Task LoadCachedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RefreshAsync(bool force, CancellationToken ct) => Task.CompletedTask;
        public Package? Find(PackageId id) => _packages.GetValueOrDefault(id);
        public PackageVersion? FindVersion(PackageId id, SemVersion version) => Find(id)?.FindVersion(version);
        public IReadOnlyList<Package> Query(CatalogQuery query) => CatalogSearch.Query(Current.Listable, query);
    }
}
