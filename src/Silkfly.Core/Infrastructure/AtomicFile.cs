using System.IO.Abstractions;
using System.Text;

namespace Silkfly.Core.Infrastructure;

public static class AtomicFile
{
    public static async Task WriteAllTextAsync(
        IFileSystem fs, string path, string contents, bool keepBackup = false, CancellationToken ct = default)
    {
        var directory = fs.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            fs.Directory.CreateDirectory(directory);

        var tmp = path + ".tmp";
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);

        await using (var stream = fs.FileStream.New(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        if (keepBackup && fs.File.Exists(path))
            fs.File.Copy(path, path + ".bak", overwrite: true);

        fs.File.Move(tmp, path, overwrite: true);
    }
}
