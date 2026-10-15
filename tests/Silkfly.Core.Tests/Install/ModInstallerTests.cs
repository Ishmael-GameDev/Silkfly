using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Loader;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Tests.Loader;

namespace Silkfly.Core.Tests.Install;

public sealed class ModInstallerTests
{
    private static readonly PackageId ModId = new("Author", "Mod");

    private readonly MockFileSystem _fs = new();
    private readonly AppPaths _paths = new(TestPath.Of(@"C:\Silkfly"));
    private readonly FlakyProfileService _profiles;
    private readonly Profile _profile;

    public ModInstallerTests()
    {
        var settings = new SettingsService(_fs, _paths, TimeProvider.System, NullLogger<SettingsService>.Instance);
        settings.Load();
        var locks = new ProfileLockStore(_fs, TimeProvider.System, NullLogger<ProfileLockStore>.Instance);
        var loader = new LoaderService(_fs, _paths, new LoaderServiceTests.FakeCatalog(), new FakePackageCache(_fs), TimeProvider.System, NullLogger<LoaderService>.Instance);
        var inner = new ProfileService(_fs, _paths, locks, loader, settings, TimeProvider.System, NullLogger<ProfileService>.Instance);
        inner.LoadAsync(default).GetAwaiter().GetResult();
        _profile = inner.CreateAsync("Test", default).GetAwaiter().GetResult();
        _profiles = new FlakyProfileService(inner);
    }

    private ModInstaller CreateInstaller() =>
        new(_fs, _profiles, new PackageLayout(), TimeProvider.System, NullLogger<ModInstaller>.Instance);

    private string Zip(string name, Dictionary<string, string> entries)
    {
        var path = TestPath.Of($@"C:\zips\{name}.zip");
        FakePackageCache.WriteZip(_fs, path, entries);
        return path;
    }

    private string P(string relative) => _fs.Path.Combine(_profile.RootPath, relative.Replace('/', Path.DirectorySeparatorChar));

    private Task<InstalledMod> Install(string zip, string version = "1.0.0", PackageId? id = null) =>
        CreateInstaller().InstallArchiveAsync(_profile, zip, id ?? ModId, Versions.Parse(version), ModSource.Thunderstore, true, default);

    [Fact]
    public async Task Forged_entry_size_is_refused_and_leaves_no_staging()
    {
        var zip = TestPath.Of(@"C:\zips\bomb.zip");
        FakePackageCache.WriteZipWithForgedSize(_fs, zip, "Mod.dll", actualBytes: 1_000_000, declaredBytes: 16);

        (await Should.ThrowAsync<SilkflyException>(() => Install(zip))).MessageKey.ShouldBe("Error_BadArchive");

        _profiles.GetLock(_profile).Find(ModId).ShouldBeNull();
        (!_fs.Directory.Exists(_profile.StagingPath) || !_fs.Directory.EnumerateFileSystemEntries(_profile.StagingPath).Any()).ShouldBeTrue();
    }

    [Fact]
    public async Task Install_places_files_and_writes_lock()
    {
        var zip = Zip("v1", new() { ["manifest.json"] = "{}", ["Mod.dll"] = "v1", ["config/mod.cfg"] = "default" });

        var mod = await Install(zip);

        _fs.File.ReadAllText(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBe("v1");
        _fs.File.ReadAllText(P("BepInEx/config/mod.cfg")).ShouldBe("default");
        mod.Files.ShouldContain("BepInEx/plugins/Author-Mod/Mod.dll");
        _profiles.GetLock(_profile).Find(ModId)!.Version.ToString().ShouldBe("1.0.0");
        _fs.Directory.Exists(_profile.StagingPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Update_replaces_files_keeps_user_config_and_drops_removed_files()
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1", ["Old.dll"] = "old", ["config/mod.cfg"] = "default" }));
        _fs.File.WriteAllText(P("BepInEx/config/mod.cfg"), "user value");

        await Install(Zip("v2", new() { ["Mod.dll"] = "v2", ["config/mod.cfg"] = "new default" }), "1.1.0");

        _fs.File.ReadAllText(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBe("v2");
        _fs.File.Exists(P("BepInEx/plugins/Author-Mod/Old.dll")).ShouldBeFalse();
        _fs.File.ReadAllText(P("BepInEx/config/mod.cfg")).ShouldBe("user value");
        _profiles.GetLock(_profile).Find(ModId)!.Version.ToString().ShouldBe("1.1.0");
    }

    [Fact]
    public async Task Failure_while_writing_lock_rolls_back_to_previous_version()
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1" }));
        _profiles.FailNextSave = true;

        await Should.ThrowAsync<IOException>(() => Install(Zip("v2", new() { ["Mod.dll"] = "v2", ["New.dll"] = "n" }), "2.0.0"));

        _fs.File.ReadAllText(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBe("v1");
        _fs.File.Exists(P("BepInEx/plugins/Author-Mod/New.dll")).ShouldBeFalse();
        _profiles.GetLock(_profile).Find(ModId)!.Version.ToString().ShouldBe("1.0.0");
        _fs.Directory.Exists(_profile.StagingPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Core_files_never_overwrite_loader_or_other_mods()
    {
        _fs.AddFile(P("BepInEx/core/BepInEx.dll"), new MockFileData("loader"));

        await Should.ThrowAsync<InstallConflictException>(() => Install(Zip("bad", new() { ["core/BepInEx.dll"] = "evil" })));
        _fs.File.ReadAllText(P("BepInEx/core/BepInEx.dll")).ShouldBe("loader");

        await Install(Zip("lib", new() { ["core/Lib.dll"] = "lib" }), id: new PackageId("A", "Lib"));
        await Should.ThrowAsync<InstallConflictException>(() => Install(Zip("other", new() { ["core/Lib.dll"] = "x" }), id: new PackageId("B", "Other")));
    }

    [Fact]
    public async Task Disable_moves_files_except_configs_and_enable_restores()
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1", ["config/mod.cfg"] = "c" }));
        var installer = CreateInstaller();

        await installer.SetEnabledAsync(_profile, ModId, false, default);

        _fs.File.Exists(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBeFalse();
        _fs.File.Exists(P("disabled/BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBeTrue();
        _fs.File.Exists(P("BepInEx/config/mod.cfg")).ShouldBeTrue();
        _fs.Directory.Exists(P("BepInEx/plugins/Author-Mod")).ShouldBeFalse();
        installer.IsIntact(_profile, _profiles.GetLock(_profile).Find(ModId)!).ShouldBeTrue();

        await installer.SetEnabledAsync(_profile, ModId, true, default);

        _fs.File.Exists(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBeTrue();
        _profiles.GetLock(_profile).Find(ModId)!.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Enabling_onto_occupied_path_is_a_conflict()
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1" }));
        var installer = CreateInstaller();
        await installer.SetEnabledAsync(_profile, ModId, false, default);
        _fs.AddFile(P("BepInEx/plugins/Author-Mod/Mod.dll"), new MockFileData("someone else"));

        await Should.ThrowAsync<InstallConflictException>(() => installer.SetEnabledAsync(_profile, ModId, true, default));
        _fs.File.ReadAllText(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBe("someone else");
    }

    [Fact]
    public async Task Update_of_disabled_mod_stays_disabled()
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1" }));
        await CreateInstaller().SetEnabledAsync(_profile, ModId, false, default);

        await Install(Zip("v2", new() { ["Mod.dll"] = "v2" }), "2.0.0");

        _fs.File.ReadAllText(P("disabled/BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBe("v2");
        _fs.File.Exists(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Uninstall_removes_files_and_configs_only_on_request(bool keepConfigs)
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1", ["config/mod.cfg"] = "c" }));

        await CreateInstaller().UninstallAsync(_profile, ModId, keepConfigs, default);

        _fs.Directory.Exists(P("BepInEx/plugins/Author-Mod")).ShouldBeFalse();
        _fs.File.Exists(P("BepInEx/config/mod.cfg")).ShouldBe(keepConfigs);
        _profiles.GetLock(_profile).Mods.ShouldBeEmpty();
        _fs.Directory.Exists(P("BepInEx/plugins")).ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_files_make_a_mod_broken()
    {
        var mod = await Install(Zip("v1", new() { ["Mod.dll"] = "v1" }));
        _fs.File.Delete(P("BepInEx/plugins/Author-Mod/Mod.dll"));

        CreateInstaller().IsIntact(_profile, mod).ShouldBeFalse();
    }

    [Fact]
    public async Task Manual_dll_is_installed_under_manual_name()
    {
        _fs.AddFile(TestPath.Of(@"C:\Downloads\Cool Mod.dll"), new MockFileData("dll"));
        var installer = CreateInstaller();

        var info = installer.IdentifyManual(TestPath.Of(@"C:\Downloads\Cool Mod.dll"), (_, _) => null);
        var mod = await installer.InstallManualAsync(_profile, TestPath.Of(@"C:\Downloads\Cool Mod.dll"), info, default);

        mod.Id.FullName.ShouldBe("manual-Cool_Mod");
        mod.Source.ShouldBe(ModSource.ManualDll);
        _fs.File.Exists(P("BepInEx/plugins/manual-Cool_Mod/Cool Mod.dll")).ShouldBeTrue();
    }

    [Fact]
    public void Manual_zip_identity_comes_from_file_name_or_catalog()
    {
        var manifest = new Dictionary<string, string> { ["manifest.json"] = """{"name":"Mod","version_number":"1.2.3"}""", ["Mod.dll"] = "x" };
        FakePackageCache.WriteZip(_fs, TestPath.Of(@"C:\d\Author-Mod-1.2.3.zip"), manifest);
        FakePackageCache.WriteZip(_fs, TestPath.Of(@"C:\d\download.zip"), manifest);
        FakePackageCache.WriteZip(_fs, TestPath.Of(@"C:\d\plain.zip"), new Dictionary<string, string> { ["Thing.dll"] = "x" });
        var installer = CreateInstaller();

        installer.IdentifyManual(TestPath.Of(@"C:\d\Author-Mod-1.2.3.zip"), (_, _) => null).Id.ShouldBe(ModId);
        installer.IdentifyManual(TestPath.Of(@"C:\d\download.zip"), (_, _) => new PackageId("Found", "Mod")).Id.ShouldBe(new PackageId("Found", "Mod"));
        installer.IdentifyManual(TestPath.Of(@"C:\d\download.zip"), (_, _) => null).Id.FullName.ShouldBe("manual-Mod");
        var plain = installer.IdentifyManual(TestPath.Of(@"C:\d\plain.zip"), (_, _) => null);
        plain.Id.FullName.ShouldBe("manual-plain");
        plain.Version.ShouldBe(Versions.Zero);
    }

    [Fact]
    public async Task Unmanaged_items_are_found_toggled_and_removed()
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1" }));
        _fs.AddFile(P("BepInEx/plugins/Loose.dll"), new MockFileData("x"));
        _fs.AddFile(P("BepInEx/plugins/SomeFolder/A.dll"), new MockFileData("y"));
        var installer = CreateInstaller();

        var items = installer.ScanUnmanaged(_profile);
        items.Select(i => i.Name).Order().ShouldBe(["Loose.dll", "SomeFolder"]);

        var folder = items.Single(i => i.IsDirectory);
        await installer.SetUnmanagedEnabledAsync(_profile, folder, false, default);
        _fs.File.Exists(P("disabled/unmanaged/SomeFolder/A.dll")).ShouldBeTrue();

        var disabled = installer.ScanUnmanaged(_profile).Single(i => i.LooksDisabled);
        await installer.SetUnmanagedEnabledAsync(_profile, disabled, true, default);
        _fs.File.Exists(P("BepInEx/plugins/SomeFolder/A.dll")).ShouldBeTrue();

        await installer.RemoveUnmanagedAsync(_profile, items.Single(i => !i.IsDirectory), default);
        _fs.File.Exists(P("BepInEx/plugins/Loose.dll")).ShouldBeFalse();
    }

    [Fact]
    public async Task Interrupted_operation_is_rolled_back_on_recovery()
    {
        await Install(Zip("v1", new() { ["Mod.dll"] = "v1" }));

        // crash mid-update: old file already in the backup, journal not committed
        var opId = Guid.NewGuid();
        _fs.AddFile(P($".staging/{opId}.backup/BepInEx/plugins/Author-Mod/Mod.dll"), new MockFileData("v1"));
        _fs.File.Delete(P("BepInEx/plugins/Author-Mod/Mod.dll"));
        var journal = new OperationJournal
        {
            OperationId = opId,
            Moves = [new("BepInEx/plugins/Author-Mod/Mod.dll", $".staging/{opId}.backup/BepInEx/plugins/Author-Mod/Mod.dll")],
        };
        await journal.SaveAsync(_fs, _profile.StagingPath, default);

        await CreateInstaller().RecoverAsync(_profile, default);

        _fs.File.ReadAllText(P("BepInEx/plugins/Author-Mod/Mod.dll")).ShouldBe("v1");
        _fs.Directory.Exists(_profile.StagingPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Unsafe_archive_changes_nothing()
    {
        var zip = Zip("evil", new() { ["Mod.dll"] = "x", ["../../evil.dll"] = "y" });

        await Should.ThrowAsync<UnsafeArchiveException>(() => Install(zip));

        _profiles.GetLock(_profile).Mods.ShouldBeEmpty();
        _fs.File.Exists(TestPath.Of(@"C:\Silkfly\evil.dll")).ShouldBeFalse();
    }

    private sealed class FlakyProfileService(IProfileService inner) : IProfileService
    {
        public bool FailNextSave { get; set; }

        public IReadOnlyList<Profile> Profiles => inner.Profiles;
        public Profile? Active => inner.Active;
        public event EventHandler? ProfilesChanged { add => inner.ProfilesChanged += value; remove => inner.ProfilesChanged -= value; }
        public event EventHandler<Profile?>? ActiveChanged { add => inner.ActiveChanged += value; remove => inner.ActiveChanged -= value; }
        public Task LoadAsync(CancellationToken ct) => inner.LoadAsync(ct);
        public Task<Profile> CreateAsync(string name, CancellationToken ct) => inner.CreateAsync(name, ct);
        public Task<Profile> DuplicateAsync(Profile source, string newName, CancellationToken ct) => inner.DuplicateAsync(source, newName, ct);
        public Task<Profile> RenameAsync(Profile profile, string newName, CancellationToken ct) => inner.RenameAsync(profile, newName, ct);
        public Task DeleteAsync(Profile profile, CancellationToken ct) => inner.DeleteAsync(profile, ct);
        public Task SetActiveAsync(Profile profile, CancellationToken ct) => inner.SetActiveAsync(profile, ct);
        public Task MarkPlayedAsync(Profile profile, CancellationToken ct) => inner.MarkPlayedAsync(profile, ct);
        public Task<Profile> SetIconAsync(Profile profile, string? sourceFile, CancellationToken ct) => inner.SetIconAsync(profile, sourceFile, ct);
        public Task MoveRootAsync(string newRoot, IProgress<double>? progress, CancellationToken ct) => inner.MoveRootAsync(newRoot, progress, ct);
        public ProfileLock GetLock(Profile profile) => inner.GetLock(profile);
        public void ValidateName(string name, Profile? except = null) => inner.ValidateName(name, except);

        public Task SaveLockAsync(Profile profile, ProfileLock lockFile, CancellationToken ct)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("disk full");
            }

            return inner.SaveLockAsync(profile, lockFile, ct);
        }
    }
}
