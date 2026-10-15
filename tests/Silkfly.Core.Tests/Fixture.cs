using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Catalog;
using Silkfly.Core.Models;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Tests;

internal static class Fixture
{
    public static string Path(params string[] parts) =>
        System.IO.Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]);

    public static string Read(params string[] parts) => File.ReadAllText(Path(parts));

    public static List<PackageDto> ListingDtos() =>
        System.Text.Json.JsonSerializer.Deserialize(
            Read("thunderstore", "package-listing.json"),
            ThunderstoreJsonContext.Default.ListPackageDto)!;

    public static IReadOnlyList<Package> ListingPackages() =>
        CatalogMapper.Map(ListingDtos(), NullLogger.Instance);
}
