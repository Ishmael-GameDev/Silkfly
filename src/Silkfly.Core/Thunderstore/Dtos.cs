using System.Text.Json.Serialization;

namespace Silkfly.Core.Thunderstore;

// Thunderstore API v1 package listing, field for field (snake_case). See docs/thunderstore-api.md.

public sealed class PackageDto
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? Owner { get; set; }
    public string? PackageUrl { get; set; }
    public string? DonationLink { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset DateUpdated { get; set; }
    public string? Uuid4 { get; set; }
    public int RatingScore { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDeprecated { get; set; }
    public bool HasNsfwContent { get; set; }
    public List<string>? Categories { get; set; }
    public List<VersionDto>? Versions { get; set; }
}

public sealed class VersionDto
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? VersionNumber { get; set; }
    public List<string>? Dependencies { get; set; }
    public string? DownloadUrl { get; set; }
    public long Downloads { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool IsActive { get; set; }
    public string? Uuid4 { get; set; }
    public long FileSize { get; set; }
}

public sealed class MarkdownDto
{
    public string? Markdown { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(List<PackageDto>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(MarkdownDto))]
internal sealed partial class ThunderstoreJsonContext : JsonSerializerContext;
