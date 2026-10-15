using System.Reflection;
using System.Runtime.InteropServices;

namespace Silkfly.App;

public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/Ishmael-GameDev/Silkfly";

    public const string NewsFeedUrl = "https://raw.githubusercontent.com/Ishmael-GameDev/Silkfly/main/news.json";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static string SystemInfo(string dataRoot, string language) =>
        string.Join(Environment.NewLine,
            $"Silkfly {Version}",
            $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"Process: {RuntimeInformation.ProcessArchitecture}",
            $"Language: {language}",
            $"Data: {dataRoot}");
}
