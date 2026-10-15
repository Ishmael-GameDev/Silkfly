using Silkfly.Core.Catalog;
using Silkfly.Core.Models;

namespace Silkfly.Core.Tests.Catalog;

public sealed class CatalogSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly Package Alpha = Make("Team-Alpha_Mod", "First thing", ["Mods"], downloads: 100, rating: 5, updated: Now.AddDays(-30));
    private static readonly Package Beta = Make("Other-Beta", "Boss rush helper", ["Boss", "Utility"], downloads: 500, rating: 1, updated: Now.AddDays(-2));
    private static readonly Package Gamma = Make("Team-Gamma", "Old stuff", ["Mods"], downloads: 50, rating: 9, updated: Now.AddDays(-1), deprecated: true);
    private static readonly Package Delta = Make("Team-Delta", "Spicy", ["Cosmetic"], downloads: 10, rating: 0, updated: Now.AddDays(-100), nsfw: true);
    private static readonly Package[] All = [Alpha, Beta, Gamma, Delta];

    private static IReadOnlyList<string> Names(CatalogQuery q) => CatalogSearch.Query(All, q).Select(p => p.Id.Name).ToList();

    [Fact]
    public void Default_hides_deprecated_and_nsfw_and_sorts_by_name() =>
        Names(new CatalogQuery { Now = Now }).ShouldBe(["Alpha_Mod", "Beta"]);

    [Fact]
    public void Deprecated_and_nsfw_can_be_shown() =>
        Names(new CatalogQuery { Now = Now, IncludeDeprecated = true, IncludeNsfw = true }).Count.ShouldBe(4);

    [Fact]
    public void Installed_deprecated_mod_stays_visible() =>
        Names(new CatalogQuery { Now = Now, StateOf = id => new ModQueryState(id == Gamma.Id, true, false) })
            .ShouldContain("Gamma");

    [Theory]
    [InlineData("alpha mod", "Alpha_Mod")] // underscore matches a space, case-insensitive
    [InlineData("boss helper", "Beta")]    // all words must match, in description
    [InlineData("utility", "Beta")]        // category
    [InlineData("other", "Beta")]          // author
    public void Text_search_matches_all_words(string text, string expected) =>
        Names(new CatalogQuery { Now = Now, Text = text }).ShouldBe([expected]);

    [Fact]
    public void Text_search_requires_every_word() =>
        Names(new CatalogQuery { Now = Now, Text = "alpha boss" }).ShouldBeEmpty();

    [Fact]
    public void Installed_enabled_and_outdated_filters_use_supplied_state()
    {
        ModQueryState State(PackageId id) =>
            id == Alpha.Id ? new ModQueryState(true, true, true) :
            id == Beta.Id ? new ModQueryState(true, false, false) : default;

        Names(new CatalogQuery { Now = Now, Filter = ModFilter.Installed, StateOf = State }).ShouldBe(["Alpha_Mod", "Beta"]);
        Names(new CatalogQuery { Now = Now, Filter = ModFilter.Enabled, StateOf = State }).ShouldBe(["Alpha_Mod"]);
        Names(new CatalogQuery { Now = Now, Filter = ModFilter.Outdated, StateOf = State }).ShouldBe(["Alpha_Mod"]);
    }

    [Fact]
    public void Whats_new_separates_new_and_updated_packages()
    {
        var fresh = Make("New-Fresh", "brand new", ["Mods"], downloads: 1, rating: 0, updated: Now.AddDays(-1));
        var fresher = fresh with { DateCreated = Now.AddDays(-3) };
        Package[] all = [Alpha, Beta, fresher];

        IReadOnlyList<string> Query(bool isNew, bool updated, int days = 7) =>
            CatalogSearch.Query(all, new CatalogQuery
            {
                Now = Now, Filter = ModFilter.WhatsNew, WhatsNewDays = days, WhatsNewIncludesNew = isNew, WhatsNewIncludesUpdated = updated,
            }).Select(p => p.Id.Name).ToList();

        Query(isNew: true, updated: false).ShouldBe(["Fresh"]);
        Query(isNew: false, updated: true).ShouldBe(["Beta"]);
        Query(isNew: true, updated: true).ShouldBe(["Beta", "Fresh"]);
        Query(isNew: true, updated: true, days: 30).ShouldBe(["Alpha_Mod", "Beta", "Fresh"]);
    }

    [Fact]
    public void Category_and_author_filters()
    {
        Names(new CatalogQuery { Now = Now, Categories = new HashSet<string> { "boss" } }).ShouldBe(["Beta"]);
        Names(new CatalogQuery { Now = Now, Author = "team" }).ShouldBe(["Alpha_Mod"]);
    }

    [Theory]
    [InlineData(SortOrder.Downloads, new[] { "Beta", "Alpha_Mod" })]
    [InlineData(SortOrder.Rating, new[] { "Alpha_Mod", "Beta" })]
    [InlineData(SortOrder.Updated, new[] { "Beta", "Alpha_Mod" })]
    public void Sort_orders(SortOrder sort, string[] expected) =>
        Names(new CatalogQuery { Now = Now, Sort = sort }).ShouldBe(expected);

    private static Package Make(
        string fullName, string description, string[] categories, long downloads, int rating, DateTimeOffset updated,
        bool deprecated = false, bool nsfw = false)
    {
        var id = PackageId.ParseFullName(fullName);
        var version = new PackageVersion(id, Versions.Parse("1.0.0"), description, null,
            new Uri("https://thunderstore.io/package/download/x/y/1.0.0/"), downloads, updated, null, [], 1, true);
        return new Package(id, id.Namespace, new Uri("https://thunderstore.io/"), null, updated.AddDays(-10), updated,
            rating, false, deprecated, nsfw, categories, [version]);
    }
}
