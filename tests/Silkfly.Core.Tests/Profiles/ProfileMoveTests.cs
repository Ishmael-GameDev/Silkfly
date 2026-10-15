using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Loader;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Tests.Loader;

namespace Silkfly.Core.Tests.Profiles;

public sealed class ProfileMoveTests
{
    private readonly MockFileSystem _fs = new();
    private readonly SettingsService _settings;
    private readonly AppPaths _paths;
    private readonly ProfileLockStore _locks;

    public ProfileMoveTests()
    {
        _settings = new SettingsService(_fs, new AppPaths(TestPath.Of(@"C:\Silkfly")), TimeProvider.System, NullLogger<SettingsService>.Instance);
        _settings.Load();
        _paths = new AppPaths(TestPath.Of(@"C:\Silkfly"), () => _settings.Current.ProfilesRoot);
        _locks = new ProfileLockStore(_fs, TimeProvider.System, NullLogger<ProfileLockStore>.Instance);
    }

    private ProfileService CreateService() =>
        new(_fs, _paths, _locks,
            new LoaderService(_fs, _paths, new LoaderServiceTests.FakeCatalog(), new FakePackageCache(_fs), TimeProvider.System, NullLogger<LoaderService>.Instance),
            _settings, TimeProvider.System, NullLogger<ProfileService>.Instance);

    [Fact]
    public async Task Profiles_move_with_their_files_and_stay_active()
    {
        var service = CreateService();
        await service.LoadAsync(default);
        var first = await service.CreateAsync("First", default);
        await service.CreateAsync("Second", default);
        _fs.AddFile(_fs.Path.Combine(first.PluginsPath, "Mod", "Mod.dll"), new MockFileData("dll"));

        await service.MoveRootAsync(TestPath.Of(@"D:\Games\SilkflyProfiles"), null, default);

        _settings.Current.ProfilesRoot.ShouldBe(TestPath.Of(@"D:\Games\SilkflyProfiles"));
        service.Profiles.Select(p => p.Name).Order().ShouldBe(["First", "Second"]);
        service.Profiles.ShouldAllBe(p => p.RootPath.StartsWith(TestPath.Of(@"D:\Games\SilkflyProfiles") + Path.DirectorySeparatorChar));
        service.Active!.Id.ShouldBe(first.Id);
        _fs.File.ReadAllText(_fs.Path.Combine(service.Active.PluginsPath, "Mod", "Mod.dll")).ShouldBe("dll");
        _fs.Directory.Exists(first.RootPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Moving_back_to_the_default_folder_clears_the_setting()
    {
        var service = CreateService();
        await service.LoadAsync(default);
        await service.CreateAsync("Only", default);
        await service.MoveRootAsync(TestPath.Of(@"D:\Profiles"), null, default);

        await service.MoveRootAsync(_paths.DefaultProfilesRoot, null, default);

        _settings.Current.ProfilesRoot.ShouldBeNull();
        service.Profiles.Single().RootPath.ShouldStartWith(_paths.DefaultProfilesRoot);
    }

    [Theory]
    [InlineData(@"C:\Silkfly\profiles\inner")]
    [InlineData(@"C:\Silkfly")]
    public async Task Nested_folders_are_refused(string target)
    {
        target = TestPath.Of(target);
        var service = CreateService();
        await service.LoadAsync(default);
        await service.CreateAsync("Only", default);

        await Should.ThrowAsync<SilkflyException>(() => service.MoveRootAsync(target, null, default));
        _settings.Current.ProfilesRoot.ShouldBeNull();
    }

    [Fact]
    public async Task A_folder_with_other_files_is_refused()
    {
        var service = CreateService();
        await service.LoadAsync(default);
        await service.CreateAsync("Only", default);
        _fs.AddFile(TestPath.Of(@"D:\Busy\notes.txt"), new MockFileData("mine"));

        await Should.ThrowAsync<SilkflyException>(() => service.MoveRootAsync(TestPath.Of(@"D:\Busy"), null, default));
        service.Profiles.Single().RootPath.ShouldStartWith(_paths.DefaultProfilesRoot);
    }
}
