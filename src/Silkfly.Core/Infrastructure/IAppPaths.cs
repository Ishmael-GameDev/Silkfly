namespace Silkfly.Core.Infrastructure;

public interface IAppPaths
{
    string DataRoot { get; }

    string CacheRoot { get; }

    string LogsRoot { get; }

    string ProfilesRoot { get; }

    string DefaultProfilesRoot { get; }

    string SettingsFile { get; }

    string LoaderCacheRoot { get; }
}
