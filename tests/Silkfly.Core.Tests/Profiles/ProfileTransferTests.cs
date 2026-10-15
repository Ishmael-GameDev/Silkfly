using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Loader;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Tests.Loader;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Tests.Profiles;

public sealed class ProfileTransferTests
{
    private readonly MockFileSystem _fs = new();
    private readonly ProfileService _profiles;

    public ProfileTransferTests()
    {
        var paths = new AppPaths(TestPath.Of(@"C:\Silkfly"));
        var settings = new SettingsService(_fs, paths, TimeProvider.System, NullLogger<SettingsService>.Instance);
        settings.Load();
        var locks = new ProfileLockStore(_fs, TimeProvider.System, NullLogger<ProfileLockStore>.Instance);
        var loader = new LoaderService(_fs, paths, new LoaderServiceTests.FakeCatalog(), new FakePackageCache(_fs), TimeProvider.System, NullLogger<LoaderService>.Instance);
        _profiles = new ProfileService(_fs, paths, locks, loader, settings, TimeProvider.System, NullLogger<ProfileService>.Instance);
        _profiles.LoadAsync(default).GetAwaiter().GetResult();
    }

    private ProfileTransfer CreateTransfer() =>
        new(_fs, _profiles, new ThunderstoreClient(new NoHttp(), NullLogger<ThunderstoreClient>.Instance), NullLogger<ProfileTransfer>.Instance);

    [Fact]
    public async Task Export_and_import_round_trip()
    {
        var source = await _profiles.CreateAsync("My Run", default);
        await _profiles.SaveLockAsync(source, _profiles.GetLock(source) with
        {
            Mods =
            [
                Mod("silksong_modding-ModMenu", "0.8.0", enabled: true),
                Mod("SFGrenade-WavLib", "1.1.9", enabled: false),
                Mod("manual-Thing", "0.0.0", enabled: true, ModSource.ManualDll),
            ],
        }, default);
        _fs.AddFile(_fs.Path.Combine(source.ConfigPath, "mod.cfg"), new MockFileData("x = 1"));
        _fs.AddFile(_fs.Path.Combine(source.ConfigPath, "sub", "other.cfg"), new MockFileData("y = 2"));

        var result = await CreateTransfer().ExportR2zAsync(source, TestPath.Of(@"C:\out\run.r2z"), default);
        var import = await CreateTransfer().ReadR2zAsync(TestPath.Of(@"C:\out\run.r2z"), default);

        result.SkippedManual.Select(i => i.FullName).ShouldBe(["manual-Thing"]);
        import.ProfileName.ShouldBe("My Run");
        import.Mods.Select(m => $"{m.Id.FullName} {m.Version} {m.Enabled}")
            .ShouldBe(["silksong_modding-ModMenu 0.8.0 True", "SFGrenade-WavLib 1.1.9 False"]);
        import.Configs.Keys.Order().ShouldBe(["mod.cfg", "sub/other.cfg"]);

        var created = await CreateTransfer().CreateFromImportAsync(import, "Copy", default);
        _fs.File.ReadAllText(_fs.Path.Combine(created.ConfigPath, "sub", "other.cfg")).ShouldBe("y = 2");
    }

    [Fact]
    public void Import_accepts_config_folder_without_BepInEx_prefix()
    {
        var path = TestPath.Of(@"C:\in\a.r2z");
        FakePackageCache.WriteZip(_fs, path, new Dictionary<string, string>
        {
            ["export.r2x"] = "profileName: Friend\nmods:\n- name: Author-Mod\n  version:\n    major: 1\n    minor: 2\n    patch: 3\n  enabled: true\n",
            ["config/author.mod.cfg"] = "z",
        });

        var import = ProfileTransfer.Parse(_fs.File.ReadAllBytes(path));

        import.ProfileName.ShouldBe("Friend");
        import.Mods.Single().Version.ToString().ShouldBe("1.2.3");
        import.Configs.Keys.ShouldBe(["author.mod.cfg"]);
    }

    [Fact]
    public void Import_without_manifest_fails()
    {
        FakePackageCache.WriteZip(_fs, TestPath.Of(@"C:\in\b.r2z"), new Dictionary<string, string> { ["readme.txt"] = "?" });

        Should.Throw<SilkflyException>(() => ProfileTransfer.Parse(_fs.File.ReadAllBytes(TestPath.Of(@"C:\in\b.r2z"))))
            .MessageKey.ShouldBe("Error_ImportNoManifest");
    }

    private static InstalledMod Mod(string fullName, string version, bool enabled, ModSource source = ModSource.Thunderstore) =>
        new(PackageId.ParseFullName(fullName), Versions.Parse(version), enabled, true, source, DateTimeOffset.UtcNow, []);

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in tests");
    }
}
