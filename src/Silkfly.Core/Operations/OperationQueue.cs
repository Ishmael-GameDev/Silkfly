using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Semver;
using Silkfly.Core.Catalog;
using Silkfly.Core.Dependencies;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Launch;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Operations;

public enum OperationKind { Install, Update, UpdateAll, Uninstall, Enable, Disable, ManualInstall }

public enum OperationPhase { Queued, Downloading, Extracting, Applying, Done, Failed, Canceled }

public sealed record OperationProgress(
    Guid OperationId, OperationKind Kind, PackageId? Target, OperationPhase Phase, double Fraction, string? Message);

public sealed record OperationCompleted(Guid OperationId, OperationKind Kind, PackageId? Target, Profile Profile, Exception? Error)
{
    public bool Canceled => Error is OperationCanceledException;
}

public interface IUserPrompts
{
    Task<bool> ConfirmDependenciesAsync(PackageId target, IReadOnlyList<PlannedInstall> extra, IReadOnlyList<ResolutionIssue> issues);

    Task<bool> ConfirmBreakingDependentsAsync(PackageId target, IReadOnlyList<InstalledMod> dependents, bool disabling);

    Task<IReadOnlyList<PackageId>> PickOrphansToRemoveAsync(IReadOnlyList<InstalledMod> orphans);

    Task<bool> ConfirmEnableDependenciesAsync(PackageId target, IReadOnlyList<InstalledMod> disabledDependencies);
}

public interface IOperationQueue
{
    Guid EnqueueInstall(Profile profile, PackageId id, SemVersion? version = null);

    Guid EnqueueUpdate(Profile profile, PackageId id);

    Guid EnqueueUpdateAll(Profile profile);

    Guid EnqueueUninstall(Profile profile, PackageId id);

    Guid EnqueueUninstallAll(Profile profile);

    Guid EnqueueSetEnabled(Profile profile, PackageId id, bool enabled);

    Guid EnqueueSetAllEnabled(Profile profile, bool enabled);

    Guid EnqueueManualInstall(Profile profile, string filePath);

    Guid EnqueueInstallSet(Profile profile, IReadOnlyList<ExportedMod> mods);

    Guid EnqueueRemoveUnmanaged(Profile profile, UnmanagedItem item);

    Guid EnqueueSetUnmanagedEnabled(Profile profile, UnmanagedItem item, bool enabled);

    Guid EnqueueLinkUnmanaged(Profile profile, UnmanagedItem item, PackageId id);

    void Cancel(Guid operationId);

    IReadOnlyCollection<OperationProgress> Active { get; }

    event EventHandler<OperationProgress>? Progress;

    event EventHandler<OperationCompleted>? Completed;
}

public sealed class OperationQueue(
    IModInstaller installer,
    IDependencyResolver resolver,
    ICatalogService catalog,
    IPackageCache packages,
    IProfileService profiles,
    ILaunchService launcher,
    ISettingsService settings,
    IUserPrompts prompts,
    ILogger<OperationQueue> logger) : IOperationQueue
{
    private const int MaxParallelDownloads = 3;

    private readonly SemaphoreSlim _downloads = new(MaxParallelDownloads, MaxParallelDownloads);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _profileGates = new();
    private readonly ConcurrentDictionary<Guid, Entry> _active = new();

    public IReadOnlyCollection<OperationProgress> Active => _active.Values.Select(e => e.Progress).ToList();

    public event EventHandler<OperationProgress>? Progress;

    public event EventHandler<OperationCompleted>? Completed;

    public Guid EnqueueInstall(Profile profile, PackageId id, SemVersion? version = null) =>
        Start(profile, OperationKind.Install, id, (op, ct) => InstallAsync(op, profile, [(id, version)], ct));

    public Guid EnqueueUpdate(Profile profile, PackageId id) =>
        Start(profile, OperationKind.Update, id, (op, ct) => UpdateAsync(op, profile, [id], ct));

    public Guid EnqueueUpdateAll(Profile profile) =>
        Start(profile, OperationKind.UpdateAll, null, (op, ct) =>
            UpdateAsync(op, profile, profiles.GetLock(profile).Mods.Where(m => m.Source != ModSource.ManualDll).Select(m => m.Id).ToList(), ct));

    public Guid EnqueueUninstall(Profile profile, PackageId id) =>
        Start(profile, OperationKind.Uninstall, id, (op, ct) => UninstallAsync(op, profile, id, ct));

    public Guid EnqueueUninstallAll(Profile profile) =>
        Start(profile, OperationKind.Uninstall, null, (op, ct) => ApplyAsync(op, profile, async () =>
        {
            foreach (var mod in profiles.GetLock(profile).Mods.ToList())
                await installer.UninstallAsync(profile, mod.Id, keepConfigs: true, CancellationToken.None).ConfigureAwait(false);
        }, ct));

    public Guid EnqueueSetEnabled(Profile profile, PackageId id, bool enabled) =>
        Start(profile, enabled ? OperationKind.Enable : OperationKind.Disable, id, (op, ct) => SetEnabledAsync(op, profile, id, enabled, ct));

    public Guid EnqueueSetAllEnabled(Profile profile, bool enabled) =>
        Start(profile, enabled ? OperationKind.Enable : OperationKind.Disable, null, async (op, ct) =>
        {
            await ApplyAsync(op, profile, async () =>
            {
                foreach (var mod in profiles.GetLock(profile).Mods.Where(m => m.Enabled != enabled))
                    await installer.SetEnabledAsync(profile, mod.Id, enabled, CancellationToken.None).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        });

    public Guid EnqueueManualInstall(Profile profile, string filePath) =>
        Start(profile, OperationKind.ManualInstall, null, (op, ct) => ManualInstallAsync(op, profile, filePath, ct));

    public Guid EnqueueInstallSet(Profile profile, IReadOnlyList<ExportedMod> mods) =>
        Start(profile, OperationKind.Install, null, async (op, ct) =>
        {
            var available = mods.Where(m => catalog.FindVersion(m.Id, m.Version) is not null).ToList();
            var missing = mods.Except(available).Select(m => $"{m.Id.DisplayName} {m.Version}").ToList();

            var needed = available
                .SelectMany(m => catalog.FindVersion(m.Id, m.Version)!.Dependencies)
                .Select(d => d.Id)
                .ToHashSet();

            var plan = resolver.PlanInstall(profiles.GetLock(profile).Mods, available.Select(m => (m.Id, (SemVersion?)m.Version)).ToList(),
                catalog.Find, DependencyPolicy.Exact);
            plan = plan with
            {
                Installs = plan.Installs.Select(i => i with { Explicit = !needed.Contains(i.Version.PackageId) }).ToList(),
            };

            await ExecutePlanAsync(op, profile, plan, [], ct, confirm: false).ConfigureAwait(false);

            var toDisable = available.Where(m => !m.Enabled).Select(m => m.Id).ToList();
            if (toDisable.Count > 0)
            {
                await ApplyAsync(op, profile, async () =>
                {
                    foreach (var id in toDisable)
                        await installer.SetEnabledAsync(profile, id, false, CancellationToken.None).ConfigureAwait(false);
                }, CancellationToken.None).ConfigureAwait(false);
            }

            if (missing.Count > 0)
                throw new PartialImportException(missing);
        });

    public Guid EnqueueRemoveUnmanaged(Profile profile, UnmanagedItem item) =>
        Start(profile, OperationKind.Uninstall, null, (op, ct) =>
            ApplyAsync(op, profile, () => installer.RemoveUnmanagedAsync(profile, item, CancellationToken.None), ct));

    public Guid EnqueueSetUnmanagedEnabled(Profile profile, UnmanagedItem item, bool enabled) =>
        Start(profile, enabled ? OperationKind.Enable : OperationKind.Disable, null, (op, ct) =>
            ApplyAsync(op, profile, () => installer.SetUnmanagedEnabledAsync(profile, item, enabled, CancellationToken.None), ct));

    public Guid EnqueueLinkUnmanaged(Profile profile, UnmanagedItem item, PackageId id) =>
        Start(profile, OperationKind.Install, id, async (op, ct) =>
        {
            await InstallAsync(op, profile, [(id, null)], ct).ConfigureAwait(false);
            await ApplyAsync(op, profile, () => installer.RemoveUnmanagedAsync(profile, item, CancellationToken.None), CancellationToken.None)
                .ConfigureAwait(false);
        });

    public void Cancel(Guid operationId)
    {
        if (_active.TryGetValue(operationId, out var entry) && entry.Progress.Phase != OperationPhase.Applying)
            entry.Cancellation.Cancel();
    }

    private Guid Start(Profile profile, OperationKind kind, PackageId? target, Func<Entry, CancellationToken, Task> work)
    {
        var entry = new Entry(Guid.NewGuid(), kind, target, profile);
        _active[entry.Id] = entry;
        Report(entry, OperationPhase.Queued, 0);

        _ = Task.Run(async () =>
        {
            Exception? error = null;
            try
            {
                await work(entry, entry.Cancellation.Token).ConfigureAwait(false);
                Report(entry, OperationPhase.Done, 1);
            }
            catch (OperationCanceledException ex)
            {
                error = ex;
                Report(entry, OperationPhase.Canceled, 0);
            }
            catch (Exception ex)
            {
                error = ex;
                logger.LogError(ex, "Operation {Kind} {Target} in profile {Profile} failed", kind, target, profile.Name);
                Report(entry, OperationPhase.Failed, 0, ex.Message);
            }
            finally
            {
                _active.TryRemove(entry.Id, out _);
                entry.Cancellation.Dispose();
            }

            Completed?.Invoke(this, new OperationCompleted(entry.Id, kind, target, profile, error));
        });

        return entry.Id;
    }

    private async Task InstallAsync(Entry op, Profile profile, IReadOnlyList<(PackageId Id, SemVersion? Version)> requested, CancellationToken ct)
    {
        var installed = profiles.GetLock(profile).Mods;
        var plan = resolver.PlanInstall(installed, requested, catalog.Find, settings.Current.DependencyPolicy);
        await ExecutePlanAsync(op, profile, plan, requested.Select(r => r.Id).ToList(), ct).ConfigureAwait(false);
    }

    private async Task UpdateAsync(Entry op, Profile profile, IReadOnlyList<PackageId> targets, CancellationToken ct)
    {
        var installed = profiles.GetLock(profile).Mods;
        var plan = resolver.PlanUpdate(installed, targets, catalog.Find, settings.Current.DependencyPolicy);
        if (plan.Installs.Count == 0)
            return;
        await ExecutePlanAsync(op, profile, plan, plan.Installs.Where(i => i.Reason == InstallReason.Requested).Select(i => i.Version.PackageId).ToList(), ct).ConfigureAwait(false);
    }

    private async Task ExecutePlanAsync(Entry op, Profile profile, ResolutionPlan plan, IReadOnlyList<PackageId> requested, CancellationToken ct, bool confirm = true)
    {
        if (plan.HasBlockingIssues(requested))
        {
            var issue = plan.Issues.First(i => requested.Contains(i.Id));
            throw new SilkflyException(issue.Kind == ResolutionIssueKind.NotInCatalog ? "Error_NotInCatalog" : "Error_VersionNotFound", issue.Detail);
        }

        var target = op.Target ?? requested.FirstOrDefault();
        if (plan.Installs.Count == 0)
            return;
        var needsConfirmation = plan.Extra.Count > 0 ||
                                plan.Issues.Any(i => i.Kind is ResolutionIssueKind.NotInCatalog or ResolutionIssueKind.ConflictingMajor or ResolutionIssueKind.VersionNotFound);
        if (confirm && needsConfirmation && !await prompts.ConfirmDependenciesAsync(target, plan.Extra, plan.Issues).ConfigureAwait(false))
            throw new OperationCanceledException();

        var archives = new string[plan.Installs.Count];
        var totalBytes = Math.Max(1, plan.Installs.Sum(i => Math.Max(1, i.Version.FileSize)));
        var doneBytes = new long[plan.Installs.Count];
        Report(op, OperationPhase.Downloading, 0);

        await Task.WhenAll(plan.Installs.Select(async (item, index) =>
        {
            await _downloads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var progress = new Progress<long>(b =>
                {
                    doneBytes[index] = b;
                    Report(op, OperationPhase.Downloading, 0.9 * doneBytes.Sum() / totalBytes);
                });
                archives[index] = await packages.GetOrDownloadAsync(item.Version, progress, ct).ConfigureAwait(false);
            }
            finally
            {
                _downloads.Release();
            }
        })).ConfigureAwait(false);

        await ApplyAsync(op, profile, async () =>
        {
            for (var i = 0; i < plan.Installs.Count; i++)
            {
                var item = plan.Installs[i];
                Report(op, OperationPhase.Applying, 0.9 + 0.1 * i / plan.Installs.Count, item.Version.PackageId.DisplayName);
                await installer.InstallArchiveAsync(profile, archives[i], item.Version.PackageId, item.Version.Version,
                    ModSource.Thunderstore, item.Explicit, CancellationToken.None).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
    }

    private async Task UninstallAsync(Entry op, Profile profile, PackageId id, CancellationToken ct)
    {
        var installed = profiles.GetLock(profile).Mods;
        var dependents = resolver.FindDependents(installed, id, catalog.FindVersion);
        if (dependents.Count > 0 && !await prompts.ConfirmBreakingDependentsAsync(id, dependents, disabling: false).ConfigureAwait(false))
            throw new OperationCanceledException();

        await ApplyAsync(op, profile, () => installer.UninstallAsync(profile, id, keepConfigs: true, CancellationToken.None), ct).ConfigureAwait(false);

        var orphans = resolver.FindOrphans(profiles.GetLock(profile).Mods, catalog.FindVersion);
        if (orphans.Count == 0)
            return;

        var toRemove = await prompts.PickOrphansToRemoveAsync(orphans).ConfigureAwait(false);
        if (toRemove.Count == 0)
            return;

        await ApplyAsync(op, profile, async () =>
        {
            foreach (var orphan in toRemove)
                await installer.UninstallAsync(profile, orphan, keepConfigs: true, CancellationToken.None).ConfigureAwait(false);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SetEnabledAsync(Entry op, Profile profile, PackageId id, bool enabled, CancellationToken ct)
    {
        var installed = profiles.GetLock(profile).Mods;
        var toChange = new List<PackageId> { id };

        if (!enabled)
        {
            var dependents = resolver.FindDependents(installed, id, catalog.FindVersion).Where(m => m.Enabled).ToList();
            if (dependents.Count > 0 && !await prompts.ConfirmBreakingDependentsAsync(id, dependents, disabling: true).ConfigureAwait(false))
                throw new OperationCanceledException();
        }
        else
        {
            var disabledDeps = DependencyClosure(installed, id).Where(m => !m.Enabled).ToList();
            if (disabledDeps.Count > 0 && await prompts.ConfirmEnableDependenciesAsync(id, disabledDeps).ConfigureAwait(false))
                toChange.InsertRange(0, disabledDeps.Select(m => m.Id));
        }

        await ApplyAsync(op, profile, async () =>
        {
            foreach (var mod in toChange)
                await installer.SetEnabledAsync(profile, mod, enabled, CancellationToken.None).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task ManualInstallAsync(Entry op, Profile profile, string filePath, CancellationToken ct)
    {
        var info = installer.IdentifyManual(filePath, (name, version) =>
            catalog.Current.Packages
                .Where(p => p.Id.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.FindVersion(version) is not null)
                .Select(p => (PackageId?)p.Id)
                .SingleOrDefault());

        await ApplyAsync(op, profile, () => installer.InstallManualAsync(profile, filePath, info, CancellationToken.None), ct).ConfigureAwait(false);
    }

    private async Task ApplyAsync(Entry op, Profile profile, Func<Task> apply, CancellationToken ct)
    {
        var gate = _profileGates.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (launcher.IsGameRunning && launcher.RunningProfile?.Id == profile.Id)
                throw new GameRunningException();

            Report(op, OperationPhase.Applying, Math.Max(op.Progress.Fraction, 0.9));
            await apply().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private List<InstalledMod> DependencyClosure(IReadOnlyCollection<InstalledMod> installed, PackageId id)
    {
        var byId = installed.ToDictionary(m => m.Id);
        var result = new Dictionary<PackageId, InstalledMod>();
        var frontier = new Queue<PackageId>([id]);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (!byId.TryGetValue(current, out var mod))
                continue;
            foreach (var dep in catalog.FindVersion(mod.Id, mod.Version)?.Dependencies ?? [])
            {
                if (!dep.IsLoaderPack && byId.TryGetValue(dep.Id, out var depMod) && result.TryAdd(dep.Id, depMod))
                    frontier.Enqueue(dep.Id);
            }
        }

        return result.Values.ToList();
    }

    private void Report(Entry op, OperationPhase phase, double fraction, string? message = null)
    {
        op.Progress = new OperationProgress(op.Id, op.Kind, op.Target, phase, fraction, message);
        Progress?.Invoke(this, op.Progress);
    }

    private sealed class Entry(Guid id, OperationKind kind, PackageId? target, Profile profile)
    {
        public Guid Id { get; } = id;
        public OperationKind Kind { get; } = kind;
        public PackageId? Target { get; } = target;
        public Profile Profile { get; } = profile;
        public CancellationTokenSource Cancellation { get; } = new();
        public OperationProgress Progress { get; set; } = new(id, kind, target, OperationPhase.Queued, 0, null);
    }
}
