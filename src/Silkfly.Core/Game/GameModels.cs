namespace Silkfly.Core.Game;

public enum GameSource { Steam, Gog, Xbox, Manual }

public enum GamePlatform { Windows, Linux, MacOS }

public sealed record GameInstall(
    string Path,
    GameSource Source,
    GamePlatform Platform,
    string ExecutablePath,
    string DataFolderPath);

public sealed record GameValidationResult(bool IsValid, GameInstall? Install, IReadOnlyList<string> Problems)
{
    public static GameValidationResult Invalid(params string[] problems) => new(false, null, problems);
}

public static class GameInfo
{
    public const string SteamAppId = "1030300";
    public const string ProductName = "Hollow Knight Silksong";
    public const string WindowsExecutable = ProductName + ".exe";
    public const string LinuxExecutable = ProductName;
    public const string MacBundle = ProductName + ".app";
    public const string DataFolder = ProductName + "_Data";

    public const string ProcessName = ProductName;

    public const string Company = "Team Cherry";

    public static IReadOnlyList<string> SaveFolderCandidates(GamePlatform host, string home) => host switch
    {
        GamePlatform.Windows => [Path.Combine(home, "AppData", "LocalLow", Company, ProductName)],
        GamePlatform.MacOS =>
        [
            Path.Combine(home, "Library", "Application Support", Company, ProductName),
            Path.Combine(home, "Library", "Application Support", $"unity.{Company}.{ProductName}"),
        ],
        _ => [Path.Combine(home, ".config", "unity3d", Company, ProductName)],
    };
}
