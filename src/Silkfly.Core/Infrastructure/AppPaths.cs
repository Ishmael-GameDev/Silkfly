namespace Silkfly.Core.Infrastructure;

public sealed class AppPaths : IAppPaths
{
    private readonly Func<string?> _profilesRootOverride;

    public AppPaths(string dataRoot, Func<string?>? profilesRootOverride = null)
    {
        DataRoot = dataRoot;
        _profilesRootOverride = profilesRootOverride ?? (() => null);
    }

    public static string DefaultDataRoot() =>
        Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } custom
            ? custom
            : PlatformDataRoot();

    public const string DataDirVariable = "SILKFLY_DATA_DIR";

    private static string PlatformDataRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "Silkfly");

    public string DataRoot { get; }

    public string CacheRoot => Path.Combine(DataRoot, "cache");

    public string LogsRoot => Path.Combine(DataRoot, "logs");

    public string ProfilesRoot =>
        _profilesRootOverride() is { Length: > 0 } custom ? custom : DefaultProfilesRoot;

    public string DefaultProfilesRoot => Path.Combine(DataRoot, "profiles");

    public string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public string LoaderCacheRoot => Path.Combine(DataRoot, "loader");
}
