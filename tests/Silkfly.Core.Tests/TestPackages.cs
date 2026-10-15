using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using Semver;
using Silkfly.Core.Install;
using Silkfly.Core.Models;

namespace Silkfly.Core.Tests;

internal sealed class FakePackageCache(IFileSystem fs) : IPackageCache
{
    private readonly Dictionary<string, string> _zips = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Downloads { get; } = [];

    public void Add(PackageId id, string version, IReadOnlyDictionary<string, string> entries)
    {
        var path = fs.Path.Combine(fs.Path.GetTempPath(), "fake-cache", $"{id.FullName}-{version}.zip");
        WriteZip(fs, path, entries);
        _zips[$"{id.FullName}-{version}"] = path;
    }

    // headers claim fewer bytes than the entry really holds
    public static void WriteZipWithForgedSize(IFileSystem fs, string path, string entryName, int actualBytes, uint declaredBytes)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = zip.CreateEntry(entryName).Open();
            writer.Write(new byte[actualBytes]);
        }

        var bytes = memory.ToArray();
        for (var i = 0; i + 4 <= bytes.Length; i++)
        {
            var signature = BitConverter.ToUInt32(bytes, i);
            if (signature == 0x04034b50)
                BitConverter.GetBytes(declaredBytes).CopyTo(bytes, i + 22);
            else if (signature == 0x02014b50)
                BitConverter.GetBytes(declaredBytes).CopyTo(bytes, i + 24);
        }

        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, bytes);
    }

    public static void WriteZip(IFileSystem fs, string path, IReadOnlyDictionary<string, string> entries)
    {
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
        using var stream = fs.File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            if (!name.EndsWith('/'))
            {
                using var writer = entry.Open();
                writer.Write(Encoding.UTF8.GetBytes(content));
            }
        }
    }

    public Task<string> GetOrDownloadAsync(PackageVersion version, IProgress<long>? bytes, CancellationToken ct)
    {
        Downloads.Add($"{version.PackageId.FullName}-{version.Version}");
        return Task.FromResult(_zips[$"{version.PackageId.FullName}-{version.Version}"]);
    }

    public string? TryGetCached(PackageId id, SemVersion version) => _zips.GetValueOrDefault($"{id.FullName}-{version}");

    public void Evict(PackageId id, SemVersion version) => _zips.Remove($"{id.FullName}-{version}");

    public long SizeBytes => 0;

    public Task TrimAsync(long maxBytes, CancellationToken ct) => Task.CompletedTask;

    public Task ClearAsync(CancellationToken ct) => Task.CompletedTask;
}

internal static class TestCatalog
{
    public static Package Package(string fullName, string version, params string[] dependencies) =>
        Package(fullName, [version], dependencies);

    public static Package Package(string fullName, string[] versions, params string[] dependencies)
    {
        var id = PackageId.ParseFullName(fullName);
        var list = versions
            .Select(v => new PackageVersion(
                id, Versions.Parse(v), "desc", null,
                new Uri($"https://thunderstore.io/package/download/{id.Namespace}/{id.Name}/{v}/"),
                1, DateTimeOffset.UtcNow, null, dependencies.Select(DependencyRef.Parse).ToList(), 0, true))
            .OrderByDescending(v => v.Version, Comparer<SemVersion>.Create(Versions.Compare))
            .ToList();
        return new Package(id, id.Namespace, new Uri("https://thunderstore.io/"), null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, false, false, false, ["Mods"], list);
    }
}
