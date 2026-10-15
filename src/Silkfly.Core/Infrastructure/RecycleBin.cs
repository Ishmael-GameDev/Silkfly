using System.IO.Abstractions;
using System.Runtime.Versioning;

namespace Silkfly.Core.Infrastructure;

public static class RecycleBin
{
    public static void DeleteDirectory(IFileSystem fs, string path)
    {
        if (!fs.Directory.Exists(path))
            return;

        if (OperatingSystem.IsWindows() && fs is FileSystem)
        {
            SendToRecycleBin(path);
            return;
        }

        fs.Directory.Delete(path, recursive: true);
    }

    [SupportedOSPlatform("windows")]
    private static void SendToRecycleBin(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}
