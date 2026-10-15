using Semver;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Dependencies;

public enum InstallReason { Requested, NewDependency, DependencyUpgrade }

public sealed record PlannedInstall(PackageVersion Version, bool Explicit, InstallReason Reason);

public enum ResolutionIssueKind { NotInCatalog, VersionNotFound, Cycle, ConflictingMajor, Deprecated }

public sealed record ResolutionIssue(ResolutionIssueKind Kind, PackageId Id, string Detail);

public sealed record ResolutionPlan(IReadOnlyList<PlannedInstall> Installs, IReadOnlyList<ResolutionIssue> Issues)
{
    public IReadOnlyList<PlannedInstall> Extra => Installs.Where(i => i.Reason != InstallReason.Requested).ToList();

    public bool HasBlockingIssues(IEnumerable<PackageId> requested) =>
        Issues.Any(i => i.Kind is ResolutionIssueKind.NotInCatalog or ResolutionIssueKind.VersionNotFound && requested.Contains(i.Id));
}

public interface IDependencyResolver
{
    ResolutionPlan PlanInstall(
        IReadOnlyCollection<InstalledMod> installed,
        IReadOnlyList<(PackageId Id, SemVersion? Version)> requested,
        Func<PackageId, Package?> catalog,
        DependencyPolicy policy);

    ResolutionPlan PlanUpdate(
        IReadOnlyCollection<InstalledMod> installed,
        IReadOnlyList<PackageId> targets,
        Func<PackageId, Package?> catalog,
        DependencyPolicy policy);

    IReadOnlyList<InstalledMod> FindDependents(
        IReadOnlyCollection<InstalledMod> installed, PackageId id, Func<PackageId, SemVersion, PackageVersion?> versionLookup);

    IReadOnlyList<InstalledMod> FindOrphans(
        IReadOnlyCollection<InstalledMod> installedAfterRemoval, Func<PackageId, SemVersion, PackageVersion?> versionLookup);
}

public sealed class DependencyResolver : IDependencyResolver
{
    public ResolutionPlan PlanInstall(
        IReadOnlyCollection<InstalledMod> installed,
        IReadOnlyList<(PackageId Id, SemVersion? Version)> requested,
        Func<PackageId, Package?> catalog,
        DependencyPolicy policy)
    {
        var state = new State(installed, catalog, policy);
        foreach (var (id, version) in requested)
        {
            if (catalog(id) is not { } package)
            {
                state.Issues.Add(new ResolutionIssue(ResolutionIssueKind.NotInCatalog, id, id.FullName));
                continue;
            }

            var chosen = version is null ? package.Latest : package.FindVersion(version);
            if (chosen is null)
            {
                state.Issues.Add(new ResolutionIssue(ResolutionIssueKind.VersionNotFound, id, version!.ToString()));
                continue;
            }

            if (state.Installed.TryGetValue(id, out var current) && current.Version.SameAs(chosen.Version))
                continue;

            state.Plan(chosen, InstallReason.Requested, isExplicit: true);
        }

        return state.Finish();
    }

    public ResolutionPlan PlanUpdate(
        IReadOnlyCollection<InstalledMod> installed,
        IReadOnlyList<PackageId> targets,
        Func<PackageId, Package?> catalog,
        DependencyPolicy policy)
    {
        var byId = installed.ToDictionary(m => m.Id);
        var requests = new List<(PackageId, SemVersion?)>();
        foreach (var id in targets)
        {
            if (byId.TryGetValue(id, out var mod) && catalog(id) is { } package && package.Latest.Version.IsNewerThan(mod.Version))
                requests.Add((id, package.Latest.Version));
        }

        var plan = PlanInstall(installed, requests, catalog, policy);

        return plan with
        {
            Installs = plan.Installs
                .Select(i => byId.TryGetValue(i.Version.PackageId, out var m) ? i with { Explicit = m.Explicit } : i)
                .ToList(),
        };
    }

    public IReadOnlyList<InstalledMod> FindDependents(
        IReadOnlyCollection<InstalledMod> installed, PackageId id, Func<PackageId, SemVersion, PackageVersion?> versionLookup)
    {
        var dependents = new Dictionary<PackageId, InstalledMod>();
        var frontier = new Queue<PackageId>([id]);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var mod in installed)
            {
                if (mod.Id == id || dependents.ContainsKey(mod.Id))
                    continue;
                if (DependenciesOf(mod, versionLookup).Any(d => d.Id == current))
                {
                    dependents[mod.Id] = mod;
                    frontier.Enqueue(mod.Id);
                }
            }
        }

        return dependents.Values.ToList();
    }

    public IReadOnlyList<InstalledMod> FindOrphans(
        IReadOnlyCollection<InstalledMod> installedAfterRemoval, Func<PackageId, SemVersion, PackageVersion?> versionLookup)
    {
        var remaining = installedAfterRemoval.ToList();
        var orphans = new List<InstalledMod>();
        while (true)
        {
            var needed = remaining.SelectMany(m => DependenciesOf(m, versionLookup)).Select(d => d.Id).ToHashSet();
            var round = remaining.Where(m => !m.Explicit && !needed.Contains(m.Id)).ToList();
            if (round.Count == 0)
                return orphans;

            orphans.AddRange(round);
            remaining.RemoveAll(round.Contains);
        }
    }

    private static IEnumerable<DependencyRef> DependenciesOf(InstalledMod mod, Func<PackageId, SemVersion, PackageVersion?> versionLookup) =>
        (versionLookup(mod.Id, mod.Version)?.Dependencies ?? []).Where(d => !d.IsLoaderPack);

    public static PackageVersion? Choose(Package package, SemVersion required, DependencyPolicy policy) => policy switch
    {
        DependencyPolicy.Exact => package.FindVersion(required),
        DependencyPolicy.Latest => package.Latest,
        _ => package.Versions.FirstOrDefault(v => v.Version.Major == required.Major && v.Version.IsAtLeast(required))
             ?? package.FindVersion(required),
    };

    private sealed class State(
        IReadOnlyCollection<InstalledMod> installed, Func<PackageId, Package?> catalog, DependencyPolicy policy)
    {
        public Dictionary<PackageId, InstalledMod> Installed { get; } = installed.ToDictionary(m => m.Id);

        public List<ResolutionIssue> Issues { get; } = [];

        private readonly Dictionary<PackageId, PlannedInstall> _planned = new();
        private readonly Dictionary<PackageId, List<SemVersion>> _requirements = new();

        public void Plan(PackageVersion version, InstallReason reason, bool isExplicit)
        {
            var id = version.PackageId;
            if (_planned.TryGetValue(id, out var existing))
            {
                if (!version.Version.IsNewerThan(existing.Version.Version))
                {
                    if (isExplicit && !existing.Explicit)
                        _planned[id] = existing with { Explicit = true, Reason = InstallReason.Requested };
                    return;
                }

                reason = existing.Reason == InstallReason.Requested ? InstallReason.Requested : reason;
                isExplicit |= existing.Explicit;
            }

            _planned[id] = new PlannedInstall(version, isExplicit, reason);
            if (catalog(id) is { IsDeprecated: true } && Issues.All(i => i.Id != id || i.Kind != ResolutionIssueKind.Deprecated))
                Issues.Add(new ResolutionIssue(ResolutionIssueKind.Deprecated, id, id.FullName));

            foreach (var dep in version.Dependencies.Where(d => !d.IsLoaderPack))
                Require(dep);
        }

        private void Require(DependencyRef dep)
        {
            var requirements = _requirements.TryGetValue(dep.Id, out var list) ? list : _requirements[dep.Id] = [];
            if (requirements.Count > 0 && requirements.Any(r => r.Major != dep.Version.Major) &&
                Issues.All(i => i.Id != dep.Id || i.Kind != ResolutionIssueKind.ConflictingMajor))
            {
                Issues.Add(new ResolutionIssue(ResolutionIssueKind.ConflictingMajor, dep.Id,
                    string.Join(", ", requirements.Append(dep.Version).Select(v => v.ToString()).Distinct())));
            }

            requirements.Add(dep.Version);

            var installedNow = Installed.GetValueOrDefault(dep.Id);
            if (!_planned.ContainsKey(dep.Id) && installedNow is not null && installedNow.Version.IsAtLeast(dep.Version))
                return;
            if (_planned.TryGetValue(dep.Id, out var planned) && planned.Version.Version.IsAtLeast(dep.Version))
                return;

            if (catalog(dep.Id) is not { } package)
            {
                if (Issues.All(i => i.Id != dep.Id || i.Kind != ResolutionIssueKind.NotInCatalog))
                    Issues.Add(new ResolutionIssue(ResolutionIssueKind.NotInCatalog, dep.Id, dep.ToString()));
                return;
            }

            var highest = requirements.Max(Comparer<SemVersion>.Create(Versions.Compare))!;
            var chosen = Choose(package, highest, policy);
            if (chosen is null)
            {
                Issues.Add(new ResolutionIssue(ResolutionIssueKind.VersionNotFound, dep.Id, highest.ToString()));
                return;
            }

            Plan(chosen, installedNow is null ? InstallReason.NewDependency : InstallReason.DependencyUpgrade, isExplicit: false);
        }

        public ResolutionPlan Finish()
        {
            var ordered = new List<PlannedInstall>();
            var color = new Dictionary<PackageId, int>(); // 0 white, 1 grey, 2 black

            void Visit(PackageId id)
            {
                if (!_planned.TryGetValue(id, out var item))
                    return;
                switch (color.GetValueOrDefault(id))
                {
                    case 2:
                        return;
                    case 1:
                        if (Issues.All(i => i.Id != id || i.Kind != ResolutionIssueKind.Cycle))
                            Issues.Add(new ResolutionIssue(ResolutionIssueKind.Cycle, id, id.FullName));
                        return;
                }

                color[id] = 1;
                foreach (var dep in item.Version.Dependencies)
                    Visit(dep.Id);
                color[id] = 2;
                ordered.Add(item);
            }

            foreach (var id in _planned.Keys.OrderBy(k => k.FullName, StringComparer.OrdinalIgnoreCase))
                Visit(id);

            return new ResolutionPlan(ordered, Issues);
        }
    }
}
