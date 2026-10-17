using Semver;

namespace Silkfly.Core.Models;

public sealed record Package(
    PackageId Id,
    string Owner,
    Uri PackageUrl,
    Uri? DonationUrl,
    DateTimeOffset DateCreated,
    DateTimeOffset DateUpdated,
    int RatingScore,
    bool IsPinned,
    bool IsDeprecated,
    bool HasNsfwContent,
    IReadOnlyList<string> Categories,
    IReadOnlyList<PackageVersion> Versions)
{
    public PackageVersion Latest => Versions[0];

    public long TotalDownloads { get; } = Versions.Sum(v => v.Downloads);

    public string DisplayName => Id.DisplayName;

    public PackageVersion? FindVersion(SemVersion version) =>
        Versions.FirstOrDefault(v => v.Version.SameAs(version));
}

public sealed record PackageVersion(
    PackageId PackageId,
    SemVersion Version,
    string Description,
    Uri? IconUrl,
    Uri DownloadUrl,
    long Downloads,
    DateTimeOffset DateCreated,
    Uri? WebsiteUrl,
    IReadOnlyList<DependencyRef> Dependencies,
    long FileSize,
    bool IsActive);

public enum CatalogSource { Empty, Cache, Network }

public sealed record CatalogSnapshot(
    IReadOnlyList<Package> Packages,
    IReadOnlyDictionary<PackageId, Package> ById,
    DateTimeOffset FetchedAt,
    CatalogSource Source)
{
    public static readonly CatalogSnapshot Empty =
        new([], new Dictionary<PackageId, Package>(), DateTimeOffset.MinValue, CatalogSource.Empty);

    public IEnumerable<Package> Listable(string? appNamespace = null) =>
        Packages.Where(p => !CatalogRules.IsHidden(p.Id, appNamespace));

    public static CatalogSnapshot Create(IEnumerable<Package> packages, DateTimeOffset fetchedAt, CatalogSource source)
    {
        var list = packages.ToList();
        var byId = new Dictionary<PackageId, Package>();
        foreach (var p in list)
            byId[p.Id] = p;
        return new CatalogSnapshot(list, byId, fetchedAt, source);
    }
}

public static class CatalogRules
{
    public const string Community = "hollow-knight-silksong";

    public static readonly PackageId LoaderPackage = new("silksong_modding", "BepInExPack_Silksong");

    private static readonly HashSet<PackageId> HiddenTools =
    [
        new("ebkr", "r2modman"),
        new("Kesomannen", "GaleModManager"),
    ];

    public const string AppPackageName = "Silkfly";

    public static bool IsHidden(PackageId id, string? appNamespace = null) =>
        id.IsLoaderPack() || HiddenTools.Contains(id) || IsApp(id, appNamespace);

    // Silkfly's own package
    private static bool IsApp(PackageId id, string? appNamespace) =>
        appNamespace is not null &&
        string.Equals(id.Namespace, appNamespace, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(id.Name, AppPackageName, StringComparison.OrdinalIgnoreCase);
}
