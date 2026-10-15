using Silkfly.Core.Game;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Launch;

public sealed record LaunchCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    bool TracksGameProcess,
    DoorstopConfigChange? ConfigTarget = null);

public sealed record DoorstopConfigChange(bool Enabled, string? TargetAssembly);

public static class LaunchCommandBuilder
{
    public static LaunchCommand Build(
        GameInstall game, LaunchMode mode, string? preloader, GamePlatform host, string? steamExecutable)
    {
        var modded = preloader is not null;
        IReadOnlyList<string> doorstopArgs = modded
            ? ["--doorstop-enabled", "true", "--doorstop-target-assembly", preloader!]
            : ["--doorstop-enabled", "false"];

        switch (mode)
        {
            case LaunchMode.ViaSteam:
                if (host == GamePlatform.Windows || game.Platform == GamePlatform.Windows)
                {
                    return new LaunchCommand(
                        steamExecutable ?? "steam", ["-applaunch", GameInfo.SteamAppId, .. doorstopArgs], null, TracksGameProcess: false);
                }

                // native Linux/macOS: launch options must be "./run_bepinex.sh %command%"
                return new LaunchCommand(
                    steamExecutable ?? "steam",
                    modded ? ["-applaunch", GameInfo.SteamAppId, .. doorstopArgs] : ["-applaunch", GameInfo.SteamAppId, "--doorstop-enabled", "false"],
                    null,
                    TracksGameProcess: false);

            case LaunchMode.ProtonConfig:
                var target = modded ? ToWinePath(preloader!) : null;
                return new LaunchCommand(
                    host == GamePlatform.MacOS ? "open" : "xdg-open",
                    [$"steam://rungameid/{GameInfo.SteamAppId}"],
                    null,
                    TracksGameProcess: false,
                    new DoorstopConfigChange(modded, target));

            default:
                return BuildDirect(game, preloader, doorstopArgs);
        }
    }

    private static LaunchCommand BuildDirect(GameInstall game, string? preloader, IReadOnlyList<string> doorstopArgs)
    {
        if (game.Platform == GamePlatform.Windows)
            return new LaunchCommand(game.ExecutablePath, doorstopArgs, game.Path, TracksGameProcess: true);

        if (preloader is null)
        {
            return game.Platform == GamePlatform.MacOS
                ? new LaunchCommand("open", [game.ExecutablePath], game.Path, TracksGameProcess: false)
                : new LaunchCommand(game.ExecutablePath, [], game.Path, TracksGameProcess: true);
        }

        var script = Path.Combine(game.Path, "run_bepinex.sh");
        return new LaunchCommand("/bin/sh", [script, game.ExecutablePath, .. doorstopArgs], game.Path, TracksGameProcess: true);
    }

    public static string ToWinePath(string linuxPath) => "Z:" + linuxPath.Replace('/', '\\');
}
