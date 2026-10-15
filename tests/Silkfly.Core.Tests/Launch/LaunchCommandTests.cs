using Silkfly.Core.Game;
using Silkfly.Core.Launch;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Tests.Launch;

public sealed class LaunchCommandTests
{
    private static readonly GameInstall WindowsGame = new(@"C:\Games\HKS", GameSource.Steam, GamePlatform.Windows,
        @"C:\Games\HKS\Hollow Knight Silksong.exe", @"C:\Games\HKS\Hollow Knight Silksong_Data");

    private static readonly GameInstall LinuxGame = new("/games/hks", GameSource.Steam, GamePlatform.Linux,
        "/games/hks/Hollow Knight Silksong", "/games/hks/Hollow Knight Silksong_Data");

    [Fact]
    public void Direct_windows_modded_passes_doorstop_arguments()
    {
        var cmd = LaunchCommandBuilder.Build(WindowsGame, LaunchMode.Direct, @"C:\P\BepInEx\core\BepInEx.Preloader.dll", GamePlatform.Windows, null);

        cmd.FileName.ShouldBe(WindowsGame.ExecutablePath);
        cmd.Arguments.ShouldBe(["--doorstop-enabled", "true", "--doorstop-target-assembly", @"C:\P\BepInEx\core\BepInEx.Preloader.dll"]);
        cmd.WorkingDirectory.ShouldBe(WindowsGame.Path);
        cmd.TracksGameProcess.ShouldBeTrue();
    }

    [Fact]
    public void Direct_windows_vanilla_disables_doorstop() =>
        LaunchCommandBuilder.Build(WindowsGame, LaunchMode.Direct, null, GamePlatform.Windows, null)
            .Arguments.ShouldBe(["--doorstop-enabled", "false"]);

    [Fact]
    public void Direct_linux_modded_runs_script()
    {
        var cmd = LaunchCommandBuilder.Build(LinuxGame, LaunchMode.Direct, "/p/BepInEx/core/BepInEx.Preloader.dll", GamePlatform.Linux, null);

        cmd.FileName.ShouldBe("/bin/sh");
        cmd.Arguments[0].ShouldEndWith("run_bepinex.sh");
        cmd.Arguments.Skip(1).ShouldBe([LinuxGame.ExecutablePath, "--doorstop-enabled", "true", "--doorstop-target-assembly", "/p/BepInEx/core/BepInEx.Preloader.dll"]);
    }

    [Fact]
    public void Direct_linux_vanilla_runs_game_without_script() =>
        LaunchCommandBuilder.Build(LinuxGame, LaunchMode.Direct, null, GamePlatform.Linux, null).FileName.ShouldBe(LinuxGame.ExecutablePath);

    [Fact]
    public void Via_steam_uses_applaunch()
    {
        var cmd = LaunchCommandBuilder.Build(WindowsGame, LaunchMode.ViaSteam, @"C:\P\pre.dll", GamePlatform.Windows, @"C:\Steam\steam.exe");

        cmd.FileName.ShouldBe(@"C:\Steam\steam.exe");
        cmd.Arguments.Take(2).ShouldBe(["-applaunch", "1030300"]);
        cmd.TracksGameProcess.ShouldBeFalse();
    }

    [Fact]
    public void Proton_config_writes_wine_path_and_opens_steam_url()
    {
        var protonGame = WindowsGame with { Path = "/steam/common/HKS" };
        var cmd = LaunchCommandBuilder.Build(protonGame, LaunchMode.ProtonConfig, "/home/u/profiles/x/BepInEx/core/BepInEx.Preloader.dll", GamePlatform.Linux, null);

        cmd.FileName.ShouldBe("xdg-open");
        cmd.Arguments.ShouldBe(["steam://rungameid/1030300"]);
        cmd.ConfigTarget.ShouldBe(new DoorstopConfigChange(true, @"Z:\home\u\profiles\x\BepInEx\core\BepInEx.Preloader.dll"));
    }
}
