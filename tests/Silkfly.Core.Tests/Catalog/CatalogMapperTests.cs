using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Catalog;
using Silkfly.Core.Models;
using Silkfly.Core.Settings;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Tests.Catalog;

public sealed class CatalogMapperTests
{
    [Fact]
    public void Maps_real_listing_fixture()
    {
        var packages = Fixture.ListingPackages();

        packages.Count.ShouldBe(Fixture.ListingDtos().Count);
        var modMenu = packages.Single(p => p.Id == new PackageId("silksong_modding", "ModMenu"));
        modMenu.Owner.ShouldBe("silksong_modding");
        modMenu.Latest.Dependencies.ShouldContain(d => d.Id.Name == "UnityHelper");
        modMenu.TotalDownloads.ShouldBe(modMenu.Versions.Sum(v => v.Downloads));
    }

    [Fact]
    public void Versions_are_sorted_newest_first_regardless_of_input_order()
    {
        var dto = Dto("A-Mod", "1.2.0", "1.10.0", "1.9.3");

        var package = CatalogMapper.Map(dto, NullLogger.Instance)!;

        package.Versions.Select(v => v.Version.ToString()).ShouldBe(["1.10.0", "1.9.3", "1.2.0"]);
        package.Latest.Version.ToString().ShouldBe("1.10.0");
    }

    [Fact]
    public void Invalid_versions_are_skipped_and_package_without_valid_versions_is_dropped()
    {
        CatalogMapper.Map(Dto("A-Mod", "1.0", "1.0.0"), NullLogger.Instance)!.Versions.Count.ShouldBe(1);
        CatalogMapper.Map(Dto("A-Mod", "banana"), NullLogger.Instance).ShouldBeNull();
    }

    [Fact]
    public void Deprecated_category_prefix_is_removed() =>
        CatalogMapper.NormalizeCategories(["(Deprecated category) Tools", "Tools", "Mods"]).ShouldBe(["Tools", "Mods"]);

    [Fact]
    public void Loader_packs_are_hidden_from_listing_but_findable()
    {
        var snapshot = CatalogSnapshot.Create(Fixture.ListingPackages(), DateTimeOffset.UtcNow, CatalogSource.Network);

        snapshot.Listable().ShouldNotContain(p => p.Id.IsLoaderPack());
        snapshot.ById.ContainsKey(CatalogRules.LoaderPackage).ShouldBeTrue();
    }

    [Fact]
    public void Own_package_is_hidden_by_the_namespace_setting()
    {
        var own = AppSettings.DefaultThunderstoreNamespace;

        CatalogRules.IsHidden(new PackageId("Ishmael", "Silkfly"), own).ShouldBeTrue();
        CatalogRules.IsHidden(new PackageId("ishmael", "silkfly"), own).ShouldBeTrue();
        CatalogRules.IsHidden(new PackageId("Someone", "Silkfly"), own).ShouldBeFalse();
        CatalogRules.IsHidden(new PackageId("Someone", "Silkfly"), "Someone").ShouldBeTrue();
        CatalogRules.IsHidden(new PackageId("Ishmael", "OtherMod"), own).ShouldBeFalse();
        new AppSettings { ThunderstoreNamespace = " " }.Normalized().ThunderstoreNamespace.ShouldBe(own);
    }

    private static PackageDto Dto(string fullName, params string[] versions) => new()
    {
        Name = fullName.Split('-')[1],
        FullName = fullName,
        Owner = fullName.Split('-')[0],
        PackageUrl = "https://thunderstore.io/c/hollow-knight-silksong/p/A/Mod/",
        Categories = ["Mods"],
        Versions = versions.Select(v => new VersionDto
        {
            VersionNumber = v,
            DownloadUrl = $"https://thunderstore.io/package/download/A/Mod/{v}/",
            Description = "desc",
            Downloads = 10,
        }).ToList(),
    };
}
