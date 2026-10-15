using System.IO.Abstractions;
using System.IO.Compression;
using Silkfly.Core.Infrastructure;

namespace Silkfly.Core.Install;

public static class SafeZip
{
    public const int MaxEntries = 20_000;
    public const long MaxUncompressedBytes = 4L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Normalize(string entryName) => entryName.Replace('\\', '/').TrimStart('/');

    public static void Validate(ZipArchive zip)
    {
        if (zip.Entries.Count > MaxEntries)
            throw new UnsafeArchiveException($"{zip.Entries.Count} entries");

        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (IsUnsafePath(entry.FullName))
                throw new UnsafeArchiveException(entry.FullName);

            total += entry.Length;
            if (total > MaxUncompressedBytes)
                throw new UnsafeArchiveException($"{total} bytes");
        }
    }

    public static bool IsUnsafePath(string entryName)
    {
        var raw = entryName.Replace('\\', '/');
        if (raw.StartsWith('/') || (raw.Length >= 2 && raw[1] == ':'))
            return true;

        foreach (var segment in raw.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is ".." or "." || segment.Contains(':'))
                return true;

            var stem = segment.Split('.')[0].TrimEnd(' ');
            if (DeviceNames.Contains(stem))
                return true;
        }

        return false;
    }

    public static string ResolveInside(IFileSystem fs, string root, string relative)
    {
        var fullRoot = fs.Path.GetFullPath(root).TrimEnd(fs.Path.DirectorySeparatorChar, fs.Path.AltDirectorySeparatorChar)
                       + fs.Path.DirectorySeparatorChar;
        var target = fs.Path.GetFullPath(fs.Path.Combine(fullRoot, relative.Replace('/', fs.Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!target.StartsWith(fullRoot, comparison))
            throw new UnsafeArchiveException(relative);
        return target;
    }

    public static ZipArchive OpenRead(IFileSystem fs, string zipPath)
    {
        var stream = fs.File.OpenRead(zipPath);
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException ex)
        {
            stream.Dispose();
            throw new SilkflyException("Error_BadArchive", ex, fs.Path.GetFileName(zipPath));
        }
    }

    public static async Task ExtractEntryAsync(IFileSystem fs, ZipArchiveEntry entry, string targetPath, CancellationToken ct)
    {
        var dir = fs.Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
            fs.Directory.CreateDirectory(dir);

        // don't trust header sizes: count the bytes and check the CRC
        await using var output = fs.File.Create(targetPath);
        var buffer = new byte[81920];
        long written = 0;
        var crc = 0xFFFFFFFFu;
        try
        {
            await using var input = entry.Open();
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                written += read;
                if (written > entry.Length)
                    throw new UnsafeArchiveException(entry.FullName);
                crc = Crc32.Update(crc, buffer.AsSpan(0, read));
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new SilkflyException("Error_BadArchive", ex, entry.FullName);
        }

        if (written != entry.Length || ~crc != entry.Crc32)
            throw new SilkflyException("Error_BadArchive", entry.FullName);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            var c = (uint)n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
