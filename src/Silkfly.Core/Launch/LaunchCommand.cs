using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Launch;

public sealed record LaunchCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    bool TracksGameProcess,
    DoorstopConfigChange? ConfigTarget = null,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record DoorstopConfigChange(bool Enabled, string? TargetAssembly);

public sealed record MacArchitectures(bool HostIsArm64, MachOArch Doorstop, MachOArch Game);

public static class LaunchCommandBuilder
{
    public static LaunchCommand Build(
        GameInstall game, LaunchMode mode, string? preloader, GamePlatform host, string? steamExecutable, MacArchitectures? mac = null)
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
                return BuildDirect(game, preloader, doorstopArgs, mac);
        }
    }

    public static string MacDoorstopPath(GameInstall game) => Path.Combine(game.Path, "libdoorstop.dylib");

    public static string MacGameBinary(GameInstall game) =>
        Path.Combine(game.ExecutablePath, "Contents", "MacOS", GameInfo.ProductName);

    private static LaunchCommand BuildDirect(GameInstall game, string? preloader, IReadOnlyList<string> doorstopArgs, MacArchitectures? mac)
    {
        if (game.Platform == GamePlatform.Windows)
            return new LaunchCommand(game.ExecutablePath, doorstopArgs, game.Path, TracksGameProcess: true);

        if (preloader is null)
        {
            return game.Platform == GamePlatform.MacOS
                ? new LaunchCommand("open", [game.ExecutablePath], game.Path, TracksGameProcess: false)
                : new LaunchCommand(game.ExecutablePath, [], game.Path, TracksGameProcess: true);
        }

        if (game.Platform == GamePlatform.MacOS && mac is { HostIsArm64: true } && !mac.Doorstop.HasFlag(MachOArch.Arm64))
            return BuildRosetta(game, preloader, mac);

        var script = Path.Combine(game.Path, "run_bepinex.sh");
        return new LaunchCommand("/bin/sh", [script, game.ExecutablePath, .. doorstopArgs], game.Path, TracksGameProcess: true);
    }

    // an x86_64-only libdoorstop.dylib can't load into the arm64 game: run its x86_64 slice under Rosetta. arch execs the
    // game in place (same process id); SIP strips DYLD_* from arch's environment, hence -e
    private static LaunchCommand BuildRosetta(GameInstall game, string preloader, MacArchitectures mac)
    {
        if (mac.Game != MachOArch.None && !mac.Game.HasFlag(MachOArch.X64))
            throw new SilkflyException("Error_AppleSiliconUnsupported");

        return new LaunchCommand(
            "/usr/bin/arch",
            ["-x86_64", "-e", $"DYLD_INSERT_LIBRARIES={MacDoorstopPath(game)}", MacGameBinary(game)],
            game.Path,
            TracksGameProcess: true,
            Environment: new Dictionary<string, string>
            {
                ["DOORSTOP_ENABLED"] = "1",
                ["DOORSTOP_TARGET_ASSEMBLY"] = preloader,
            });
    }

    public static string ToWinePath(string linuxPath) => "Z:" + linuxPath.Replace('/', '\\');
}
