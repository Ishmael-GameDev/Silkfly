using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;

namespace Silkfly.Core.Catalog;

public sealed class CatalogCache(IFileSystem fs, IAppPaths paths, ILogger<CatalogCache> logger)
{
    public const int Schema = 1;

    private string CatalogFile => fs.Path.Combine(paths.CacheRoot, "catalog.json");
    private string MetaFile => fs.Path.Combine(paths.CacheRoot, "catalog.meta.json");

    public async Task<CatalogSnapshot?> LoadAsync(CancellationToken ct)
    {
        if (!fs.File.Exists(CatalogFile) || !fs.File.Exists(MetaFile))
            return null;

        try
        {
            CacheMetaDto? meta;
            await using (var metaStream = fs.File.OpenRead(MetaFile))
                meta = await JsonSerializer.DeserializeAsync(metaStream, CatalogCacheJsonContext.Default.CacheMetaDto, ct).ConfigureAwait(false);

            if (meta is null || meta.Schema != Schema)
            {
                logger.LogInformation("Catalog cache schema {Schema} is not {Expected}, ignoring it", meta?.Schema, Schema);
                return null;
            }

            CacheCatalogDto? catalog;
            await using (var stream = fs.File.OpenRead(CatalogFile))
                catalog = await JsonSerializer.DeserializeAsync(stream, CatalogCacheJsonContext.Default.CacheCatalogDto, ct).ConfigureAwait(false);

            if (catalog is null)
                return null;

            var packages = catalog.Packages.Select(FromDto).OfType<Package>();
            return CatalogSnapshot.Create(packages, meta.FetchedAt, CatalogSource.Cache);
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException)
        {
            logger.LogWarning(ex, "Catalog cache is unreadable, ignoring it");
            return null;
        }
    }

    public async Task SaveAsync(CatalogSnapshot snapshot, CancellationToken ct)
    {
        var catalog = new CacheCatalogDto
        {
            Schema = Schema,
            Community = CatalogRules.Community,
            Packages = snapshot.Packages.Select(ToDto).ToList(),
        };
        var meta = new CacheMetaDto { Schema = Schema, FetchedAt = snapshot.FetchedAt, Source = "network" };

        await AtomicFile.WriteAllTextAsync(fs, CatalogFile,
            JsonSerializer.Serialize(catalog, CatalogCacheJsonContext.Default.CacheCatalogDto), ct: ct).ConfigureAwait(false);
        await AtomicFile.WriteAllTextAsync(fs, MetaFile,
            JsonSerializer.Serialize(meta, CatalogCacheJsonContext.Default.CacheMetaDto), ct: ct).ConfigureAwait(false);
    }

    private static CachePackageDto ToDto(Package p) => new()
    {
        Ns = p.Id.Namespace,
        Name = p.Id.Name,
        Owner = p.Owner,
        Url = p.PackageUrl.AbsoluteUri,
        Donation = p.DonationUrl?.AbsoluteUri,
        Created = p.DateCreated,
        Updated = p.DateUpdated,
        Rating = p.RatingScore,
        Pinned = p.IsPinned,
        Deprecated = p.IsDeprecated,
        Nsfw = p.HasNsfwContent,
        Categories = p.Categories.ToList(),
        Versions = p.Versions.Select(v => new CacheVersionDto
        {
            V = v.Version.ToString(),
            Desc = v.Description,
            Icon = v.IconUrl?.AbsoluteUri,
            Dl = v.DownloadUrl.AbsoluteUri,
            Downloads = v.Downloads,
            Created = v.DateCreated,
            Web = v.WebsiteUrl?.AbsoluteUri,
            Deps = v.Dependencies.Select(d => d.ToString()).ToList(),
            Size = v.FileSize,
            Active = v.IsActive,
        }).ToList(),
    };

    private static Package? FromDto(CachePackageDto dto)
    {
        var id = new PackageId(dto.Ns, dto.Name);
        var versions = dto.Versions
            .Where(v => Versions.TryParse(v.V, out _))
            .Select(v => new PackageVersion(
                id,
                Versions.Parse(v.V),
                v.Desc ?? string.Empty,
                v.Icon is null ? null : new Uri(v.Icon),
                new Uri(v.Dl),
                v.Downloads,
                v.Created,
                v.Web is null ? null : new Uri(v.Web),
                v.Deps.Select(d => DependencyRef.TryParse(d, out var dep) ? dep : null).OfType<DependencyRef>().ToList(),
                v.Size,
                v.Active))
            .OrderByDescending(v => v.Version, Comparer<Semver.SemVersion>.Create(Versions.Compare))
            .ToList();

        if (versions.Count == 0)
            return null;

        return new Package(
            id, dto.Owner, new Uri(dto.Url), dto.Donation is null ? null : new Uri(dto.Donation),
            dto.Created, dto.Updated, dto.Rating, dto.Pinned, dto.Deprecated, dto.Nsfw, dto.Categories, versions);
    }
}

internal sealed class CacheMetaDto
{
    public int Schema { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public string? Source { get; set; }
}

internal sealed class CacheCatalogDto
{
    public int Schema { get; set; }
    public string Community { get; set; } = string.Empty;
    public List<CachePackageDto> Packages { get; set; } = [];
}

internal sealed class CachePackageDto
{
    public string Ns { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Donation { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
    public int Rating { get; set; }
    public bool Pinned { get; set; }
    public bool Deprecated { get; set; }
    public bool Nsfw { get; set; }
    public List<string> Categories { get; set; } = [];
    public List<CacheVersionDto> Versions { get; set; } = [];
}

internal sealed class CacheVersionDto
{
    public string V { get; set; } = string.Empty;
    public string? Desc { get; set; }
    public string? Icon { get; set; }
    public string Dl { get; set; } = string.Empty;
    public long Downloads { get; set; }
    public DateTimeOffset Created { get; set; }
    public string? Web { get; set; }
    public List<string> Deps { get; set; } = [];
    public long Size { get; set; }
    public bool Active { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CacheCatalogDto))]
[JsonSerializable(typeof(CacheMetaDto))]
internal sealed partial class CatalogCacheJsonContext : JsonSerializerContext;
