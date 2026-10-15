using Semver;
using Silkfly.Core.Models;

namespace Silkfly.Core.Thunderstore;

public interface IThunderstoreClient
{
    Task<IReadOnlyList<PackageDto>> FetchCommunityPackagesAsync(
        string community, IProgress<double>? progress, CancellationToken ct);

    Task<string?> GetReadmeAsync(PackageId id, SemVersion version, CancellationToken ct);

    Task<string?> GetChangelogAsync(PackageId id, SemVersion version, CancellationToken ct);

    Task DownloadAsync(Uri downloadUrl, Stream destination, long expectedSize,
        IProgress<long>? bytesProgress, CancellationToken ct);

    Task<string> CreateProfileCodeAsync(byte[] r2zBytes, CancellationToken ct);

    Task<byte[]> GetProfileByCodeAsync(string code, CancellationToken ct);
}
