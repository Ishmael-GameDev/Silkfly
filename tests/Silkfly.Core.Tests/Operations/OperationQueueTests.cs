using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Dependencies;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Launch;
using Silkfly.Core.Loader;
using Silkfly.Core.Models;
using Silkfly.Core.Operations;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Tests.Loader;

namespace Silkfly.Core.Tests.Operations;

public sealed class OperationQueueTests
{
    private readonly MockFileSystem _fs = new();
    private readonly LoaderServiceTests.FakeCatalog _catalog = new();
    private readonly FakePackageCache _cache;
    private readonly ProfileService _profiles;
    private readonly FakePrompts _prompts = new();
    private readonly FakeLauncher _launcher = new();
    private readonly Profile _profile;

    public OperationQueueTests()
    {
        var paths = new AppPaths(TestPath.Of(@"C:\Silkfly"));
        _cache = new FakePackageCache(_fs);
        var settings = new SettingsService(_fs, paths, TimeProvider.System, NullLogger<SettingsService>.Instance);
        settings.Load();
        var locks = new ProfileLockStore(_fs, TimeProvider.System, NullLogger<ProfileLockStore>.Instance);
        var loader = new LoaderService(_fs, paths, _catalog, _cache, TimeProvider.System, NullLogger<LoaderService>.Instance);
        _profiles = new ProfileService(_fs, paths, locks, loader, settings, TimeProvider.System, NullLogger<ProfileService>.Instance);
        _profiles.LoadAsync(default).GetAwaiter().GetResult();
        _profile = _profiles.CreateAsync("Test", default).GetAwaiter().GetResult();
        Settings = settings;

        AddPackage("A-Top", "1.0.0", "A-Lib-1.0.0", "BepInEx-BepInExPack_Silksong-5.4.2304");
        AddPackage("A-Lib", "1.0.0");
    }

    private SettingsService Settings { get; }

    private void AddPackage(string fullName, string version, params string[] deps)
    {
        var package = TestCatalog.Package(fullName, version, deps);
        _catalog.Add(package);
        _cache.Add(package.Id, version, new Dictionary<string, string> { [$"{package.Id.Name}.dll"] = fullName });
    }

    private OperationQueue CreateQueue() =>
        new(new ModInstaller(_fs, _profiles, new PackageLayout(), TimeProvider.System, NullLogger<ModInstaller>.Instance),
            new DependencyResolver(), _catalog, _cache, _profiles, _launcher, Settings, _prompts, NullLogger<OperationQueue>.Instance);

    private static async Task<OperationCompleted> RunAsync(OperationQueue queue, Func<OperationQueue, Guid> start)
    {
        var done = new TaskCompletionSource<OperationCompleted>();
        queue.Completed += (_, e) => done.TrySetResult(e);
        start(queue);
        return await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Install_asks_about_dependencies_and_installs_them_first()
    {
        var result = await RunAsync(CreateQueue(), q => q.EnqueueInstall(_profile, new PackageId("A", "Top")));

        result.Error.ShouldBeNull();
        _prompts.ConfirmedExtra.ShouldBe(["Lib"]);
        var mods = _profiles.GetLock(_profile).Mods;
        mods.Select(m => m.Id.Name).Order().ShouldBe(["Lib", "Top"]);
        mods.Single(m => m.Id.Name == "Lib").Explicit.ShouldBeFalse();
    }

    [Fact]
    public async Task Declining_dependencies_cancels_without_changes()
    {
        _prompts.AcceptDependencies = false;

        var result = await RunAsync(CreateQueue(), q => q.EnqueueInstall(_profile, new PackageId("A", "Top")));

        result.Canceled.ShouldBeTrue();
        _profiles.GetLock(_profile).Mods.ShouldBeEmpty();
    }

    [Fact]
    public async Task Uninstall_warns_about_dependents_and_offers_orphans()
    {
        var queue = CreateQueue();
        await RunAsync(queue, q => q.EnqueueInstall(_profile, new PackageId("A", "Top")));

        var libResult = await RunAsync(CreateQueue(), q => q.EnqueueUninstall(_profile, new PackageId("A", "Lib")));
        libResult.Error.ShouldBeNull();
        _prompts.WarnedDependents.ShouldBe(["Top"]);

        await RunAsync(CreateQueue(), q => q.EnqueueInstall(_profile, new PackageId("A", "Lib")));
        _profiles.GetLock(_profile).Find(new PackageId("A", "Lib"))!.Explicit.ShouldBeTrue();
    }

    [Fact]
    public async Task Removing_a_mod_removes_chosen_orphans()
    {
        await RunAsync(CreateQueue(), q => q.EnqueueInstall(_profile, new PackageId("A", "Top")));

        await RunAsync(CreateQueue(), q => q.EnqueueUninstall(_profile, new PackageId("A", "Top")));

        _prompts.OfferedOrphans.ShouldBe(["Lib"]);
        _profiles.GetLock(_profile).Mods.ShouldBeEmpty();
    }

    [Fact]
    public async Task Uninstall_all_removes_every_mod_without_asking()
    {
        await RunAsync(CreateQueue(), q => q.EnqueueInstall(_profile, new PackageId("A", "Top")));

        var result = await RunAsync(CreateQueue(), q => q.EnqueueUninstallAll(_profile));

        result.Error.ShouldBeNull();
        _profiles.GetLock(_profile).Mods.ShouldBeEmpty();
        _prompts.WarnedDependents.ShouldBeEmpty();
        _prompts.OfferedOrphans.ShouldBeEmpty();
    }

    [Fact]
    public async Task Linking_installs_the_package_then_removes_the_manual_copy()
    {
        var manual = _fs.Path.Combine(_profile.PluginsPath, "Lib.dll");
        _fs.AddFile(manual, new System.IO.Abstractions.TestingHelpers.MockFileData("old"));
        var item = new UnmanagedItem("BepInEx/plugins/Lib.dll", IsDirectory: false, LooksDisabled: false);

        var result = await RunAsync(CreateQueue(), q => q.EnqueueLinkUnmanaged(_profile, item, new PackageId("A", "Lib")));

        result.Error.ShouldBeNull();
        _profiles.GetLock(_profile).Find(new PackageId("A", "Lib")).ShouldNotBeNull();
        _fs.File.Exists(manual).ShouldBeFalse();
    }

    [Fact]
    public async Task Declined_link_keeps_the_manual_copy()
    {
        _prompts.AcceptDependencies = false;
        var manual = _fs.Path.Combine(_profile.PluginsPath, "Top.dll");
        _fs.AddFile(manual, new System.IO.Abstractions.TestingHelpers.MockFileData("old"));
        var item = new UnmanagedItem("BepInEx/plugins/Top.dll", IsDirectory: false, LooksDisabled: false);

        var result = await RunAsync(CreateQueue(), q => q.EnqueueLinkUnmanaged(_profile, item, new PackageId("A", "Top")));

        result.Canceled.ShouldBeTrue();
        _fs.File.Exists(manual).ShouldBeTrue();
    }

    [Fact]
    public async Task Changes_are_blocked_while_the_game_runs_with_the_profile()
    {
        _launcher.Running = _profile;

        var result = await RunAsync(CreateQueue(), q => q.EnqueueInstall(_profile, new PackageId("A", "Lib")));

        result.Error.ShouldBeOfType<GameRunningException>();
        _profiles.GetLock(_profile).Mods.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_all_updates_outdated_mods()
    {
        await RunAsync(CreateQueue(), q => q.EnqueueInstall(_profile, new PackageId("A", "Lib")));
        var newer = TestCatalog.Package("A-Lib", ["1.0.0", "1.1.0"]);
        _catalog.Add(newer);
        _cache.Add(newer.Id, "1.1.0", new Dictionary<string, string> { ["Lib.dll"] = "1.1.0" });

        var result = await RunAsync(CreateQueue(), q => q.EnqueueUpdateAll(_profile));

        result.Error.ShouldBeNull();
        _profiles.GetLock(_profile).Find(new PackageId("A", "Lib"))!.Version.ToString().ShouldBe("1.1.0");
    }

    private sealed class FakePrompts : IUserPrompts
    {
        public bool AcceptDependencies { get; set; } = true;
        public List<string> ConfirmedExtra { get; } = [];
        public List<string> WarnedDependents { get; } = [];
        public List<string> OfferedOrphans { get; } = [];

        public Task<bool> ConfirmDependenciesAsync(PackageId target, IReadOnlyList<PlannedInstall> extra, IReadOnlyList<ResolutionIssue> issues)
        {
            ConfirmedExtra.AddRange(extra.Select(e => e.Version.PackageId.Name));
            return Task.FromResult(AcceptDependencies);
        }

        public Task<bool> ConfirmBreakingDependentsAsync(PackageId target, IReadOnlyList<InstalledMod> dependents, bool disabling)
        {
            WarnedDependents.AddRange(dependents.Select(d => d.Id.Name));
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<PackageId>> PickOrphansToRemoveAsync(IReadOnlyList<InstalledMod> orphans)
        {
            OfferedOrphans.AddRange(orphans.Select(o => o.Id.Name));
            return Task.FromResult<IReadOnlyList<PackageId>>(orphans.Select(o => o.Id).ToList());
        }

        public Task<bool> ConfirmEnableDependenciesAsync(PackageId target, IReadOnlyList<InstalledMod> disabledDependencies) =>
            Task.FromResult(true);
    }

    private sealed class FakeLauncher : ILaunchService
    {
        public Profile? Running { get; set; }
        public bool IsGameRunning => Running is not null;
        public Profile? RunningProfile => Running;
        public event EventHandler? RunningChanged { add { } remove { } }
        public Task<LaunchResult> LaunchModdedAsync(Profile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task<LaunchResult> LaunchVanillaAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
