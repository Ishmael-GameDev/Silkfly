using Silkfly.Core.Models;

namespace Silkfly.Core.Tests.Models;

public sealed class IdentityTests
{
    [Theory]
    [InlineData("silksong_modding-ModMenu", "silksong_modding", "ModMenu")]
    [InlineData("Some-Team-Mod_Name", "Some-Team", "Mod_Name")]
    public void PackageId_splits_on_last_dash(string fullName, string ns, string name)
    {
        var id = PackageId.ParseFullName(fullName);

        id.Namespace.ShouldBe(ns);
        id.Name.ShouldBe(name);
        id.FullName.ShouldBe(fullName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NoDash")]
    [InlineData("-Name")]
    [InlineData("Ns-")]
    [InlineData(null)]
    public void PackageId_rejects_invalid(string? s) =>
        PackageId.TryParseFullName(s, out _).ShouldBeFalse();

    [Fact]
    public void PackageId_display_name_replaces_underscores() =>
        new PackageId("a", "Show_Damage_HealthBar").DisplayName.ShouldBe("Show Damage HealthBar");

    [Fact]
    public void PackageId_equality_ignores_case()
    {
        var a = new PackageId("MonoDetour", "MonoDetour");
        var b = new PackageId("monodetour", "MONODETOUR");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void DependencyRef_parses_from_the_right()
    {
        var dep = DependencyRef.Parse("MonoDetour-MonoDetour_BepInEx_5-0.7.16");

        dep.Id.ShouldBe(new PackageId("MonoDetour", "MonoDetour_BepInEx_5"));
        dep.Version.ToString().ShouldBe("0.7.16");
        dep.ToString().ShouldBe("MonoDetour-MonoDetour_BepInEx_5-0.7.16");
    }

    [Theory]
    [InlineData("Ns-Name")]
    [InlineData("Ns-Name-1.2")]
    [InlineData("Ns-Name-x.y.z")]
    [InlineData("Name-1.0.0")]
    [InlineData("")]
    public void DependencyRef_rejects_invalid(string s) =>
        DependencyRef.TryParse(s, out _).ShouldBeFalse();

    [Theory]
    [InlineData("BepInEx-BepInExPack-5.4.2100", true)]
    [InlineData("BepInEx-BepInExPack_Silksong-5.4.2304", true)]
    [InlineData("silksong_modding-BepInExPack_Silksong-1.0.3", true)]
    [InlineData("silksong_modding-ModMenu-0.8.0", false)]
    public void DependencyRef_detects_loader_packs(string s, bool isLoader) =>
        DependencyRef.Parse(s).IsLoaderPack.ShouldBe(isLoader);

    [Fact]
    public void Versions_compare_numerically()
    {
        Versions.Parse("1.10.0").IsNewerThan(Versions.Parse("1.9.9")).ShouldBeTrue();
        Versions.Parse("2.0.0").IsAtLeast(Versions.Parse("2.0.0")).ShouldBeTrue();
        Versions.TryParse("1.0", out _).ShouldBeFalse();
    }
}
