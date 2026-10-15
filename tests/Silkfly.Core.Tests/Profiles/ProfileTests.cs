using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Loader;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Tests.Loader;

namespace Silkfly.Core.Tests.Profiles;

public sealed class ProfileTests
{
    private readonly MockFileSystem _fs = new();
    private readonly AppPaths _paths = new(TestPath.Of(@"C:\Silkfly"));
    private readonly ProfileLockStore _locks;
    private readonly SettingsService _settings;

    public ProfileTests()
    {
        _locks = new ProfileLockStore(_fs, TimeProvider.System, NullLogger<ProfileLockStore>.Instance);
        _settings = new SettingsService(_fs, _paths, TimeProvider.System, NullLogger<SettingsService>.Instance);
        _settings.Load();
    }

    private ProfileService CreateService() =>
        new(_fs, _paths, _locks,
            new LoaderService(_fs, _paths, new LoaderServiceTests.FakeCatalog(), new FakePackageCache(_fs), TimeProvider.System, NullLogger<LoaderService>.Instance),
            _settings, TimeProvider.System, NullLogger<ProfileService>.Instance);

    [Fact]
    public async Task Create_makes_folders_lock_and_becomes_active()
    {
        var service = CreateService();
        await service.LoadAsync(default);

        var profile = await service.CreateAsync("  Default  ", default);

        profile.Name.ShouldBe("Default");
        _fs.Path.GetFileName(profile.RootPath).ShouldBe(profile.Id.ToString());
        _fs.Directory.Exists(profile.PluginsPath).ShouldBeTrue();
        _fs.File.Exists(profile.LockPath).ShouldBeTrue();
        service.Active.ShouldBe(profile);
        _settings.Current.ActiveProfileId.ShouldBe(profile.Id);
    }

    [Fact]
    public async Task Profiles_survive_reload_with_active_one()
    {
        var first = CreateService();
        await first.LoadAsync(default);
        await first.CreateAsync("A", default);
        var b = await first.CreateAsync("B", default);
        await first.SetActiveAsync(b, default);

        var second = CreateService();
        await second.LoadAsync(default);

        second.Profiles.Select(p => p.Name).ShouldBe(["A", "B"]);
        second.Active!.Id.ShouldBe(b.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("default")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345")]
    public async Task Invalid_names_are_rejected(string name)
    {
        var service = CreateService();
        await service.LoadAsync(default);
        await service.CreateAsync("Default", default);

        await Should.ThrowAsync<SilkflyException>(() => service.CreateAsync(name, default));
    }

    [Fact]
    public async Task Rename_changes_name_only()
    {
        var service = CreateService();
        await service.LoadAsync(default);
        var profile = await service.CreateAsync("Old", default);

        var renamed = await service.RenameAsync(profile, "New", default);

        renamed.RootPath.ShouldBe(profile.RootPath);
        _locks.Load(profile.RootPath)!.Name.ShouldBe("New");
        service.Active!.Name.ShouldBe("New");
    }

    [Fact]
    public async Task Icon_is_copied_into_the_profile_and_can_be_removed()
    {
        var service = CreateService();
        await service.LoadAsync(default);
        var profile = await service.CreateAsync("Iconic", default);
        _fs.AddFile(TestPath.Of(@"C:\Pictures\fly.PNG"), new MockFileData([1, 2, 3]));

        var withIcon = await service.SetIconAsync(profile, TestPath.Of(@"C:\Pictures\fly.PNG"), default);

        withIcon.IconPath.ShouldBe(_fs.Path.Combine(profile.RootPath, "icon.png"));
        _fs.File.ReadAllBytes(withIcon.IconPath!).ShouldBe(new byte[] { 1, 2, 3 });
        _locks.Load(profile.RootPath)!.Icon.ShouldBe("icon.png");

        var without = await service.SetIconAsync(withIcon, null, default);

        without.IconPath.ShouldBeNull();
        _fs.File.Exists(_fs.Path.Combine(profile.RootPath, "icon.png")).ShouldBeFalse();
        _locks.Load(profile.RootPath)!.Icon.ShouldBeNull();
    }

    [Theory]
    [InlineData(@"C:\Pictures\fly.gif", 10)]
    [InlineData(@"C:\Pictures\huge.png", ProfileService.MaxIconBytes + 1)]
    public async Task Unsupported_or_huge_icons_are_rejected(string file, long size)
    {
        file = TestPath.Of(file);
        var service = CreateService();
        await service.LoadAsync(default);
        var profile = await service.CreateAsync("Iconic", default);
        _fs.AddFile(file, new MockFileData(new byte[size]));

        await Should.ThrowAsync<SilkflyException>(() => service.SetIconAsync(profile, file, default));
        _locks.Load(profile.RootPath)!.Icon.ShouldBeNull();
    }

    [Fact]
    public void Icon_outside_the_profile_folder_is_ignored() =>
        new Profile(Guid.NewGuid(), "P", TestPath.Of(@"C:\Silkfly\profiles\p"), DateTimeOffset.UtcNow, null, Path.Combine("..", "..", "evil.png")).IconPath.ShouldBeNull();

    [Fact]
    public async Task Delete_switches_active_to_remaining_profile()
    {
        var service = CreateService();
        await service.LoadAsync(default);
        var a = await service.CreateAsync("A", default);
        var b = await service.CreateAsync("B", default);

        await service.DeleteAsync(a, default);

        _fs.Directory.Exists(a.RootPath).ShouldBeFalse();
        service.Profiles.ShouldBe([b]);
        service.Active.ShouldBe(b);
    }

    [Fact]
    public async Task Duplicate_copies_files_and_lock()
    {
        var service = CreateService();
        await service.LoadAsync(default);
        var source = await service.CreateAsync("Main", default);
        _fs.AddFile(_fs.Path.Combine(source.ConfigPath, "mod.cfg"), new MockFileData("x=1"));

        var copy = await service.DuplicateAsync(source, "Copy", default);

        _fs.File.ReadAllText(_fs.Path.Combine(copy.ConfigPath, "mod.cfg")).ShouldBe("x=1");
        _locks.Load(copy.RootPath)!.Id.ShouldBe(copy.Id);
    }

    [Fact]
    public async Task Lock_round_trip_with_mods()
    {
        var root = TestPath.Of(@"C:\Silkfly\profiles\x");
        var mod = new InstalledMod(new PackageId("Author", "Mod"), Versions.Parse("1.3.1"), true, true,
            ModSource.Thunderstore, DateTimeOffset.UtcNow, ["BepInEx/plugins/Author-Mod/Mod.dll"]);
        var original = new ProfileLock { Id = Guid.NewGuid(), Name = "X", CreatedAt = DateTimeOffset.UtcNow, Mods = [mod] };

        await _locks.SaveAsync(root, original, default);
        var loaded = _locks.Load(root)!;

        loaded.Mods.Single().Id.ShouldBe(mod.Id);
        loaded.Mods.Single().Version.ToString().ShouldBe("1.3.1");
        loaded.Mods.Single().Files.ShouldBe(mod.Files);
        _fs.File.ReadAllText(_fs.Path.Combine(root, Profile.LockFileName)).ShouldContain("\"source\": \"thunderstore\"");
    }

    [Fact]
    public async Task Corrupt_lock_is_restored_from_backup()
    {
        var root = TestPath.Of(@"C:\Silkfly\profiles\x");
        var first = new ProfileLock { Id = Guid.NewGuid(), Name = "First", CreatedAt = DateTimeOffset.UtcNow };
        await _locks.SaveAsync(root, first, default);
        await _locks.SaveAsync(root, first with { Name = "Second" }, default); // .bak now holds "First"
        _fs.File.WriteAllText(_fs.Path.Combine(root, Profile.LockFileName), "{ broken");
        LockRepair? repair = null;
        _locks.LockRepaired += (_, r) => repair = r;

        var loaded = _locks.Load(root)!;

        loaded.Name.ShouldBe("First");
        repair.ShouldNotBeNull();
        repair.FromBackup.ShouldBeTrue();
        _fs.Directory.GetFiles(root, "*.corrupt-*").ShouldBe([repair.CorruptCopy]);
    }

    [Fact]
    public void Corrupt_lock_without_backup_becomes_empty()
    {
        var id = Guid.NewGuid();
        var root = TestPath.Of($@"C:\Silkfly\profiles\{id}");
        _fs.AddFile(_fs.Path.Combine(root, Profile.LockFileName), new MockFileData("nonsense"));
        LockRepair? repair = null;
        _locks.LockRepaired += (_, r) => repair = r;

        var loaded = _locks.Load(root)!;

        loaded.Id.ShouldBe(id);
        loaded.Mods.ShouldBeEmpty();
        repair!.FromBackup.ShouldBeFalse();
        repair.ProfileRoot.ShouldBe(root);
    }
}
