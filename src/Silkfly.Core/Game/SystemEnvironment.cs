using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Silkfly.Core.Game;

public interface ISystemEnvironment
{
    GamePlatform HostPlatform { get; }

    Architecture HostArchitecture { get; }

    string HomeDirectory { get; }

    IReadOnlyList<string> FixedDriveRoots { get; }

    string? GetRegistryValue(RegistryRoot root, string keyPath, string valueName);

    IReadOnlyList<string> GetRegistrySubKeys(RegistryRoot root, string keyPath);
}

public enum RegistryRoot { CurrentUser, LocalMachine }

public sealed class SystemEnvironment : ISystemEnvironment
{
    public GamePlatform HostPlatform =>
        OperatingSystem.IsWindows() ? GamePlatform.Windows :
        OperatingSystem.IsMacOS() ? GamePlatform.MacOS :
        GamePlatform.Linux;

    public Architecture HostArchitecture => RuntimeInformation.OSArchitecture;

    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public IReadOnlyList<string> FixedDriveRoots =>
        OperatingSystem.IsWindows()
            ? DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList()
            : [];

    public string? GetRegistryValue(RegistryRoot root, string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        using var key = Open(root, keyPath);
        return key?.GetValue(valueName)?.ToString();
    }

    public IReadOnlyList<string> GetRegistrySubKeys(RegistryRoot root, string keyPath)
    {
        if (!OperatingSystem.IsWindows())
            return [];
        using var key = Open(root, keyPath);
        return key?.GetSubKeyNames() ?? [];
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? Open(RegistryRoot root, string keyPath)
    {
        var hive = root == RegistryRoot.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
        try
        {
            return hive.OpenSubKey(keyPath);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
