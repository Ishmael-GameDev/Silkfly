using Microsoft.Extensions.Logging;
using Silkfly.Core.Models;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Catalog;

public static class CatalogMapper
{
    private const string DeprecatedCategoryPrefix = "(Deprecated category) ";

    public static IReadOnlyList<Package> Map(IEnumerable<PackageDto> dtos, ILogger logger)
    {
        var result = new List<Package>();
        foreach (var dto in dtos)
        {
            var package = Map(dto, logger);
            if (package is not null)
                result.Add(package);
        }

        return result;
    }

    public static Package? Map(PackageDto dto, ILogger logger)
    {
        if (!PackageId.TryParseFullName(dto.FullName, out var id) ||
            !Uri.TryCreate(dto.PackageUrl, UriKind.Absolute, out var packageUrl))
        {
            logger.LogWarning("Skipping package with invalid name or URL: {FullName}", dto.FullName);
            return null;
        }

        var versions = new List<PackageVersion>();
        foreach (var v in dto.Versions ?? [])
        {
            if (!Versions.TryParse(v.VersionNumber, out var version) ||
                !Uri.TryCreate(v.DownloadUrl, UriKind.Absolute, out var downloadUrl))
            {
                logger.LogWarning("Skipping version {Version} of {Package}: invalid version or download URL", v.VersionNumber, id);
                continue;
            }

            var dependencies = new List<DependencyRef>();
            foreach (var d in v.Dependencies ?? [])
            {
                if (DependencyRef.TryParse(d, out var dep))
                    dependencies.Add(dep);
                else
                    logger.LogWarning("Ignoring invalid dependency '{Dependency}' of {Package} {Version}", d, id, version);
            }

            versions.Add(new PackageVersion(
                id,
                version,
                v.Description ?? string.Empty,
                ParseUri(v.Icon),
                downloadUrl,
                v.Downloads,
                v.DateCreated,
                ParseUri(v.WebsiteUrl),
                dependencies,
                v.FileSize,
                v.IsActive));
        }

        if (versions.Count == 0)
        {
            logger.LogWarning("Skipping package {Package}: no valid versions", id);
            return null;
        }

        versions.Sort((a, b) => Versions.Compare(b.Version, a.Version));

        return new Package(
            id,
            dto.Owner ?? id.Namespace,
            packageUrl,
            ParseUri(dto.DonationLink),
            dto.DateCreated,
            dto.DateUpdated,
            dto.RatingScore,
            dto.IsPinned,
            dto.IsDeprecated,
            dto.HasNsfwContent,
            NormalizeCategories(dto.Categories),
            versions);
    }

    public static IReadOnlyList<string> NormalizeCategories(IEnumerable<string>? categories) =>
        (categories ?? [])
        .Select(c => c.StartsWith(DeprecatedCategoryPrefix, StringComparison.Ordinal) ? c[DeprecatedCategoryPrefix.Length..] : c)
        .Select(c => c.Trim())
        .Where(c => c.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static Uri? ParseUri(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;
}
