using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace Silkfly.Core.Game;

public interface IGameLocator
{
    IReadOnlyList<GameInstall> DetectInstalls();

    GameValidationResult Validate(string path, GameSource source = GameSource.Manual);
}

public sealed class GameLocator(IFileSystem fs, ISystemEnvironment env, ILogger<GameLocator> logger) : IGameLocator
{
    public IReadOnlyList<GameInstall> DetectInstalls()
    {
        var candidates = new List<(string Path, GameSource Source)>();
        candidates.AddRange(SteamCandidates().Select(p => (p, GameSource.Steam)));
        if (env.HostPlatform == GamePlatform.Windows)
        {
            candidates.AddRange(GogCandidates().Select(p => (p, GameSource.Gog)));
            candidates.AddRange(XboxCandidates().Select(p => (p, GameSource.Xbox)));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GameInstall>();
        foreach (var (path, source) in candidates)
        {
            if (!fs.Directory.Exists(path))
                continue;

            var validation = Validate(path, source);
            if (validation.Install is { } install && seen.Add(Normalize(install.Path)))
                result.Add(install);
        }

        logger.LogInformation("Game detection found {Count} installation(s)", result.Count);
        return result;
    }

    public GameValidationResult Validate(string path, GameSource source = GameSource.Manual)
    {
        if (string.IsNullOrWhiteSpace(path))
            return GameValidationResult.Invalid("Game_Problem_NotFound");

        path = path.Trim().TrimEnd('/', '\\');
        if (path.Length == 0)
            path = "/";

        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && fs.Directory.Exists(path))
            path = fs.Path.GetDirectoryName(path) ?? path;
        else if (fs.File.Exists(path))
            path = fs.Path.GetDirectoryName(path) ?? path;

        if (!fs.Directory.Exists(path))
            return GameValidationResult.Invalid("Game_Problem_NotFound");

        var exe = fs.Path.Combine(path, GameInfo.WindowsExecutable);
        var bundle = fs.Path.Combine(path, GameInfo.MacBundle);
        var elf = fs.Path.Combine(path, GameInfo.LinuxExecutable);

        GamePlatform platform;
        string executable, dataFolder;
        if (fs.File.Exists(exe))
        {
            platform = GamePlatform.Windows;
            executable = exe;
            dataFolder = fs.Path.Combine(path, GameInfo.DataFolder);
        }
        else if (fs.Directory.Exists(bundle))
        {
            platform = GamePlatform.MacOS;
            executable = bundle;
            dataFolder = fs.Path.Combine(bundle, "Contents", "Resources", "Data");
        }
        else if (fs.File.Exists(elf) && IsElf(elf))
        {
            platform = GamePlatform.Linux;
            executable = elf;
            dataFolder = fs.Path.Combine(path, GameInfo.DataFolder);
        }
        else
        {
            return GameValidationResult.Invalid("Game_Problem_NoExecutable");
        }

        if (!fs.Directory.Exists(dataFolder))
            return GameValidationResult.Invalid("Game_Problem_NoDataFolder");

        if (!fs.File.Exists(fs.Path.Combine(dataFolder, "Managed", "Assembly-CSharp.dll")))
            return GameValidationResult.Invalid("Game_Problem_NotMono");

        var install = new GameInstall(path, source, platform, executable, dataFolder);
        return new GameValidationResult(true, install, []);
    }

    private IEnumerable<string> SteamCandidates()
    {
        foreach (var steamRoot in SteamRoots())
        {
            var libraries = new List<string> { steamRoot };
            var libraryFile = fs.Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (fs.File.Exists(libraryFile))
            {
                try
                {
                    var vdf = VdfParser.Parse(fs.File.ReadAllText(libraryFile));
                    var folders = vdf["libraryfolders"];
                    if (folders is not null)
                    {
                        libraries.AddRange(folders.Children.Values
                            .Select(f => f.GetString("path") ?? f.Value)
                            .OfType<string>());
                    }
                }
                catch (FormatException ex)
                {
                    logger.LogWarning(ex, "Cannot parse {File}", libraryFile);
                }
            }

            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manifest = fs.Path.Combine(library, "steamapps", $"appmanifest_{GameInfo.SteamAppId}.acf");
                var installDir = GameInfo.ProductName;
                if (fs.File.Exists(manifest))
                {
                    try
                    {
                        installDir = VdfParser.Parse(fs.File.ReadAllText(manifest))["AppState"]?.GetString("installdir") ?? installDir;
                    }
                    catch (FormatException ex)
                    {
                        logger.LogWarning(ex, "Cannot parse {File}", manifest);
                    }
                }

                yield return fs.Path.Combine(library, "steamapps", "common", installDir);
            }
        }
    }

    private IEnumerable<string> SteamRoots()
    {
        var home = env.HomeDirectory;
        switch (env.HostPlatform)
        {
            case GamePlatform.Windows:
                if (env.GetRegistryValue(RegistryRoot.CurrentUser, @"Software\Valve\Steam", "SteamPath") is { } user)
                    yield return user.Replace('/', '\\');
                if (env.GetRegistryValue(RegistryRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") is { } machine)
                    yield return machine;
                break;
            case GamePlatform.Linux:
                yield return fs.Path.Combine(home, ".local", "share", "Steam");
                yield return fs.Path.Combine(home, ".steam", "steam");
                yield return fs.Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
                break;
            case GamePlatform.MacOS:
                yield return fs.Path.Combine(home, "Library", "Application Support", "Steam");
                break;
        }
    }

    private IEnumerable<string> GogCandidates()
    {
        const string gogKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        foreach (var id in env.GetRegistrySubKeys(RegistryRoot.LocalMachine, gogKey))
        {
            var name = env.GetRegistryValue(RegistryRoot.LocalMachine, $@"{gogKey}\{id}", "gameName");
            if (name?.Contains("Silksong", StringComparison.OrdinalIgnoreCase) == true &&
                env.GetRegistryValue(RegistryRoot.LocalMachine, $@"{gogKey}\{id}", "path") is { } path)
            {
                yield return path;
            }
        }

        foreach (var drive in env.FixedDriveRoots)
        {
            yield return fs.Path.Combine(drive, "GOG Games", GameInfo.ProductName);
            yield return fs.Path.Combine(drive, "Program Files (x86)", "GOG Galaxy", "Games", GameInfo.ProductName);
        }
    }

    private IEnumerable<string> XboxCandidates() =>
        env.FixedDriveRoots.Select(drive => fs.Path.Combine(drive, "XboxGames", GameInfo.ProductName, "Content"));

    private bool IsElf(string path)
    {
        try
        {
            using var stream = fs.File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == 4 && magic[0] == 0x7F && magic[1] == (byte)'E' && magic[2] == (byte)'L' && magic[3] == (byte)'F';
        }
        catch (IOException)
        {
            return false;
        }
    }

    private string Normalize(string path) => fs.Path.GetFullPath(path).TrimEnd('/', '\\');
}
