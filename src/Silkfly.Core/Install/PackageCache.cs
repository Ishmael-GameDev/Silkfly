using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Semver;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Install;

public interface IPackageCache
{
    Task<string> GetOrDownloadAsync(PackageVersion version, IProgress<long>? bytes, CancellationToken ct);

    string? TryGetCached(PackageId id, SemVersion version);

    void Evict(PackageId id, SemVersion version);

    long SizeBytes { get; }

    Task TrimAsync(long maxBytes, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}

public sealed class PackageCache(
    IFileSystem fs,
    IAppPaths paths,
    IThunderstoreClient client,
    TimeProvider time,
    ILogger<PackageCache> logger) : IPackageCache
{
    public const long DefaultMaxBytes = 2L * 1024 * 1024 * 1024;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    private string Folder => fs.Path.Combine(paths.CacheRoot, "packages");
    private string IndexFile => fs.Path.Combine(Folder, "index.json");

    public long SizeBytes =>
        fs.Directory.Exists(Folder) ? fs.Directory.GetFiles(Folder, "*.zip").Sum(f => fs.FileInfo.New(f).Length) : 0;

    public string? TryGetCached(PackageId id, SemVersion version)
    {
        var path = PathFor(id, version);
        return fs.File.Exists(path) ? path : null;
    }

    public async Task<string> GetOrDownloadAsync(PackageVersion version, IProgress<long>? bytes, CancellationToken ct)
    {
        var path = PathFor(version.PackageId, version.Version);
        var gate = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!fs.File.Exists(path))
                await DownloadAsync(version, path, bytes, ct).ConfigureAwait(false);

            await TouchAsync(fs.Path.GetFileName(path), fs.FileInfo.New(path).Length, ct).ConfigureAwait(false);
            return path;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DownloadAsync(PackageVersion version, string path, IProgress<long>? bytes, CancellationToken ct)
    {
        fs.Directory.CreateDirectory(Folder);
        var part = path + ".part";
        logger.LogInformation("Downloading {Package} {Version} ({Size} bytes)", version.PackageId, version.Version, version.FileSize);

        try
        {
            await using (var stream = fs.File.Create(part))
                await client.DownloadAsync(version.DownloadUrl, stream, version.FileSize, bytes, ct).ConfigureAwait(false);

            using (SafeZip.OpenRead(fs, part))
            {
            }

            fs.File.Move(part, path, overwrite: true);
        }
        catch
        {
            if (fs.File.Exists(part))
                fs.File.Delete(part);
            throw;
        }
    }

    public void Evict(PackageId id, SemVersion version)
    {
        var path = PathFor(id, version);
        if (fs.File.Exists(path))
            fs.File.Delete(path);
    }

    public async Task TrimAsync(long maxBytes, CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var index = LoadIndex();
            var files = fs.Directory.Exists(Folder) ? fs.Directory.GetFiles(Folder, "*.zip") : [];
            var ordered = files
                .Select(f => (Path: f, Name: fs.Path.GetFileName(f), Size: fs.FileInfo.New(f).Length))
                .OrderBy(f => index.Entries.TryGetValue(f.Name, out var e) ? e.LastAccess : DateTimeOffset.MinValue)
                .ToList();

            var total = ordered.Sum(f => f.Size);
            foreach (var file in ordered)
            {
                if (total <= maxBytes)
                    break;
                fs.File.Delete(file.Path);
                index.Entries.Remove(file.Name);
                total -= file.Size;
            }

            await SaveIndexAsync(index, ct).ConfigureAwait(false);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (fs.Directory.Exists(Folder))
                fs.Directory.Delete(Folder, recursive: true);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private string PathFor(PackageId id, SemVersion version) => fs.Path.Combine(Folder, $"{id.FullName}-{version}.zip");

    private async Task TouchAsync(string name, long size, CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var index = LoadIndex();
            index.Entries[name] = new CacheIndexEntry { LastAccess = time.GetUtcNow(), Size = size };
            await SaveIndexAsync(index, ct).ConfigureAwait(false);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private CacheIndex LoadIndex()
    {
        if (!fs.File.Exists(IndexFile))
            return new CacheIndex();
        try
        {
            return JsonSerializer.Deserialize(fs.File.ReadAllText(IndexFile), PackageCacheJsonContext.Default.CacheIndex) ?? new CacheIndex();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Package cache index is corrupt, starting a new one");
            return new CacheIndex();
        }
    }

    private Task SaveIndexAsync(CacheIndex index, CancellationToken ct) =>
        AtomicFile.WriteAllTextAsync(fs, IndexFile, JsonSerializer.Serialize(index, PackageCacheJsonContext.Default.CacheIndex), ct: ct);
}

internal sealed class CacheIndex
{
    public Dictionary<string, CacheIndexEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CacheIndexEntry
{
    public DateTimeOffset LastAccess { get; set; }
    public long Size { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CacheIndex))]
internal sealed partial class PackageCacheJsonContext : JsonSerializerContext;
