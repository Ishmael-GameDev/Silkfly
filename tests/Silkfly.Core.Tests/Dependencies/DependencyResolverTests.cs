using Silkfly.Core.Dependencies;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Tests.Dependencies;

public sealed class DependencyResolverTests
{
    private readonly Dictionary<PackageId, Package> _catalog = new();
    private readonly DependencyResolver _resolver = new();

    private void Add(string fullName, string[] versions, params string[] deps) =>
        _catalog[PackageId.ParseFullName(fullName)] = TestCatalog.Package(fullName, versions, deps);

    private ResolutionPlan Install(string fullName, DependencyPolicy policy = DependencyPolicy.LatestSameMajor, params InstalledMod[] installed) =>
        _resolver.PlanInstall(installed, [(PackageId.ParseFullName(fullName), null)], id => _catalog.GetValueOrDefault(id), policy);

    private static InstalledMod Installed(string fullName, string version, bool isExplicit = true) =>
        new(PackageId.ParseFullName(fullName), Versions.Parse(version), true, isExplicit, ModSource.Thunderstore, DateTimeOffset.UtcNow, []);

    private static string[] Names(ResolutionPlan plan) => plan.Installs.Select(i => $"{i.Version.PackageId.Name} {i.Version.Version}").ToArray();

    [Fact]
    public void Chain_is_sorted_dependencies_first()
    {
        Add("A-Top", ["1.0.0"], "A-Mid-1.0.0");
        Add("A-Mid", ["1.0.0"], "A-Base-1.0.0");
        Add("A-Base", ["1.0.0"]);

        var plan = Install("A-Top");

        Names(plan).ShouldBe(["Base 1.0.0", "Mid 1.0.0", "Top 1.0.0"]);
        plan.Installs.Single(i => i.Version.PackageId.Name == "Top").Explicit.ShouldBeTrue();
        plan.Installs.Where(i => i.Version.PackageId.Name != "Top").ShouldAllBe(i => !i.Explicit && i.Reason == InstallReason.NewDependency);
        plan.Extra.Count.ShouldBe(2);
    }

    [Fact]
    public void Diamond_takes_highest_requirement_once()
    {
        Add("A-Top", ["1.0.0"], "A-Left-1.0.0", "A-Right-1.0.0");
        Add("A-Left", ["1.0.0"], "A-Lib-1.1.0");
        Add("A-Right", ["1.0.0"], "A-Lib-1.3.0");
        Add("A-Lib", ["1.1.0", "1.3.0", "1.4.0", "2.0.0"]);

        var plan = Install("A-Top");

        plan.Installs.Count(i => i.Version.PackageId.Name == "Lib").ShouldBe(1);
        plan.Installs.Single(i => i.Version.PackageId.Name == "Lib").Version.Version.ToString().ShouldBe("1.4.0");
        Names(plan)[0].ShouldStartWith("Lib");
    }

    [Theory]
    [InlineData(DependencyPolicy.Exact, "1.1.0")]
    [InlineData(DependencyPolicy.LatestSameMajor, "1.4.0")]
    [InlineData(DependencyPolicy.Latest, "2.0.0")]
    public void Policies_choose_versions(DependencyPolicy policy, string expected)
    {
        Add("A-Top", ["1.0.0"], "A-Lib-1.1.0");
        Add("A-Lib", ["1.1.0", "1.4.0", "2.0.0"]);

        Install("A-Top", policy).Installs.Single(i => i.Version.PackageId.Name == "Lib").Version.Version.ToString().ShouldBe(expected);
    }

    [Fact]
    public void Same_major_falls_back_to_exact_version()
    {
        Add("A-Top", ["1.0.0"], "A-Lib-3.0.0");
        Add("A-Lib", ["3.0.0", "4.0.0"]);

        Install("A-Top").Installs.Single(i => i.Version.PackageId.Name == "Lib").Version.Version.ToString().ShouldBe("3.0.0");
    }

    [Fact]
    public void Installed_new_enough_dependency_is_kept()
    {
        Add("A-Top", ["1.0.0"], "A-Lib-1.1.0");
        Add("A-Lib", ["1.1.0", "1.5.0"]);

        Names(Install("A-Top", installed: Installed("A-Lib", "1.2.0", isExplicit: false))).ShouldBe(["Top 1.0.0"]);
    }

    [Fact]
    public void Installed_old_dependency_is_upgraded()
    {
        Add("A-Top", ["1.0.0"], "A-Lib-1.3.0");
        Add("A-Lib", ["1.1.0", "1.3.0"]);

        var plan = Install("A-Top", installed: Installed("A-Lib", "1.1.0"));

        plan.Installs.Single(i => i.Version.PackageId.Name == "Lib").Reason.ShouldBe(InstallReason.DependencyUpgrade);
    }

    [Fact]
    public void Conflicting_majors_are_reported_and_highest_wins()
    {
        Add("A-Top", ["1.0.0"], "A-Left-1.0.0", "A-Right-1.0.0");
        Add("A-Left", ["1.0.0"], "A-Lib-1.0.0");
        Add("A-Right", ["1.0.0"], "A-Lib-2.0.0");
        Add("A-Lib", ["1.0.0", "2.0.0"]);

        var plan = Install("A-Top");

        plan.Issues.ShouldContain(i => i.Kind == ResolutionIssueKind.ConflictingMajor);
        plan.Installs.Single(i => i.Version.PackageId.Name == "Lib").Version.Version.Major.ShouldBe(2);
    }

    [Fact]
    public void Cycles_are_reported_but_installable()
    {
        Add("A-One", ["1.0.0"], "A-Two-1.0.0");
        Add("A-Two", ["1.0.0"], "A-One-1.0.0");

        var plan = Install("A-One");

        plan.Issues.ShouldContain(i => i.Kind == ResolutionIssueKind.Cycle);
        plan.Installs.Count.ShouldBe(2);
    }

    [Fact]
    public void Loader_dependencies_are_ignored_and_missing_ones_reported()
    {
        Add("A-Top", ["1.0.0"], "BepInEx-BepInExPack-5.4.2100", "A-Ghost-1.0.0");

        var plan = Install("A-Top");

        Names(plan).ShouldBe(["Top 1.0.0"]);
        plan.Issues.ShouldContain(i => i.Kind == ResolutionIssueKind.NotInCatalog && i.Id.Name == "Ghost");
    }

    [Fact]
    public void Requested_missing_package_is_blocking() =>
        Install("A-Nope").HasBlockingIssues([new PackageId("A", "Nope")]).ShouldBeTrue();

    [Fact]
    public void Already_installed_same_version_is_skipped()
    {
        Add("A-Top", ["1.0.0"]);

        Install("A-Top", installed: Installed("A-Top", "1.0.0")).Installs.ShouldBeEmpty();
    }

    [Fact]
    public void Deprecated_package_is_a_warning()
    {
        Add("A-Top", ["1.0.0"]);
        _catalog[new PackageId("A", "Top")] = _catalog[new PackageId("A", "Top")] with { IsDeprecated = true };

        var plan = Install("A-Top");

        plan.Installs.Count.ShouldBe(1);
        plan.Issues.ShouldContain(i => i.Kind == ResolutionIssueKind.Deprecated);
    }

    [Fact]
    public void Update_keeps_explicit_flag_and_skips_current_mods()
    {
        Add("A-Top", ["1.0.0", "1.1.0"], "A-Lib-1.0.0");
        Add("A-Lib", ["1.0.0"]);
        Add("A-Same", ["2.0.0"]);

        var plan = _resolver.PlanUpdate(
            [Installed("A-Top", "1.0.0", isExplicit: false), Installed("A-Lib", "1.0.0"), Installed("A-Same", "2.0.0")],
            [new("A", "Top"), new("A", "Same")],
            id => _catalog.GetValueOrDefault(id),
            DependencyPolicy.LatestSameMajor);

        Names(plan).ShouldBe(["Top 1.1.0"]);
        plan.Installs[0].Explicit.ShouldBeFalse();
    }

    [Fact]
    public void Dependents_are_transitive()
    {
        Add("A-Top", ["1.0.0"], "A-Mid-1.0.0");
        Add("A-Mid", ["1.0.0"], "A-Base-1.0.0");
        Add("A-Base", ["1.0.0"]);
        Add("A-Other", ["1.0.0"]);
        InstalledMod[] installed = [Installed("A-Top", "1.0.0"), Installed("A-Mid", "1.0.0"), Installed("A-Base", "1.0.0"), Installed("A-Other", "1.0.0")];

        var dependents = _resolver.FindDependents(installed, new PackageId("A", "Base"), (id, v) => _catalog.GetValueOrDefault(id)?.FindVersion(v));

        dependents.Select(d => d.Id.Name).Order().ShouldBe(["Mid", "Top"]);
    }

    [Fact]
    public void Orphans_are_found_to_a_fixed_point()
    {
        Add("A-Mid", ["1.0.0"], "A-Base-1.0.0");
        Add("A-Base", ["1.0.0"]);
        Add("A-Kept", ["1.0.0"]);

        var orphans = _resolver.FindOrphans(
            [Installed("A-Mid", "1.0.0", false), Installed("A-Base", "1.0.0", false), Installed("A-Kept", "1.0.0", true)],
            (id, v) => _catalog.GetValueOrDefault(id)?.FindVersion(v));

        orphans.Select(o => o.Id.Name).Order().ShouldBe(["Base", "Mid"]);
    }
}
