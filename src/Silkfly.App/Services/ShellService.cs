using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.Services;

public sealed class ShellService(ILogger<ShellService> logger) : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Open(path);
    }

    public void OpenUrl(Uri url)
    {
        if (url.Scheme is not ("https" or "http"))
        {
            logger.LogWarning("Refusing to open non-web URL {Url}", url);
            return;
        }

        Open(url.AbsoluteUri);
    }

    public void OpenFile(string path) => Open(path);

    public void RevealInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
            Start("explorer.exe", $"/select,\"{path}\"");
        else if (OperatingSystem.IsMacOS())
            Start("open", $"-R \"{path}\"");
        else
            Open(Path.GetDirectoryName(path) ?? path);
    }

    private void Open(string target)
    {
        if (OperatingSystem.IsWindows())
            Start(new ProcessStartInfo(target) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Start(new ProcessStartInfo("open") { ArgumentList = { target } });
        else
            Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target } });
    }

    private void Start(string fileName, string arguments) =>
        Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false });

    private void Start(ProcessStartInfo info)
    {
        try
        {
            using var _ = Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Could not open {Target}", info.FileName);
        }
    }
}
