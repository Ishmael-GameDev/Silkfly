using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Launch;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Tests.Launch;

public sealed class LaunchCommandTests
{
    private static readonly GameInstall WindowsGame = new(@"C:\Games\HKS", GameSource.Steam, GamePlatform.Windows,
        @"C:\Games\HKS\Hollow Knight Silksong.exe", @"C:\Games\HKS\Hollow Knight Silksong_Data");

    private static readonly GameInstall LinuxGame = new("/games/hks", GameSource.Steam, GamePlatform.Linux,
        "/games/hks/Hollow Knight Silksong", "/games/hks/Hollow Knight Silksong_Data");

    private static readonly GameInstall MacGame = new("/games/hks", GameSource.Steam, GamePlatform.MacOS,
        "/games/hks/Hollow Knight Silksong.app", "/games/hks/Hollow Knight Silksong.app/Contents/Resources/Data");

    private const string MacPreloader = "/profiles/p/BepInEx/core/BepInEx.Preloader.dll";

    private static readonly MacArchitectures AppleSilicon = new(HostIsArm64: true, MachOArch.X64, MachOArch.X64 | MachOArch.Arm64);

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
    public void Direct_mac_apple_silicon_runs_x86_64_slice_with_doorstop_environment()
    {
        var cmd = LaunchCommandBuilder.Build(MacGame, LaunchMode.Direct, MacPreloader, GamePlatform.MacOS, null, AppleSilicon);

        cmd.FileName.ShouldBe("/usr/bin/arch");
        cmd.Arguments.ShouldBe([
            "-x86_64",
            "-e", $"DYLD_INSERT_LIBRARIES={Path.Combine(MacGame.Path, "libdoorstop.dylib")}",
            Path.Combine(MacGame.ExecutablePath, "Contents", "MacOS", "Hollow Knight Silksong"),
        ]);
        cmd.Environment.ShouldNotBeNull();
        cmd.Environment.ShouldBe(new Dictionary<string, string>
        {
            ["DOORSTOP_ENABLED"] = "1",
            ["DOORSTOP_TARGET_ASSEMBLY"] = MacPreloader,
        });
        cmd.WorkingDirectory.ShouldBe(MacGame.Path);
        cmd.TracksGameProcess.ShouldBeTrue();
    }

    [Fact]
    public void Direct_mac_intel_runs_script()
    {
        var intel = new MacArchitectures(HostIsArm64: false, MachOArch.X64, MachOArch.X64 | MachOArch.Arm64);
        var cmd = LaunchCommandBuilder.Build(MacGame, LaunchMode.Direct, MacPreloader, GamePlatform.MacOS, null, intel);

        cmd.FileName.ShouldBe("/bin/sh");
        cmd.Arguments[0].ShouldEndWith("run_bepinex.sh");
        cmd.Environment.ShouldBeNull();
    }

    [Fact]
    public void Direct_mac_apple_silicon_runs_script_when_doorstop_has_arm64()
    {
        var universal = AppleSilicon with { Doorstop = MachOArch.X64 | MachOArch.Arm64 };

        LaunchCommandBuilder.Build(MacGame, LaunchMode.Direct, MacPreloader, GamePlatform.MacOS, null, universal)
            .FileName.ShouldBe("/bin/sh");
    }

    [Fact]
    public void Direct_mac_apple_silicon_fails_when_game_has_no_x86_64()
    {
        var armOnly = AppleSilicon with { Game = MachOArch.Arm64 };

        Should.Throw<SilkflyException>(() => LaunchCommandBuilder.Build(MacGame, LaunchMode.Direct, MacPreloader, GamePlatform.MacOS, null, armOnly))
            .MessageKey.ShouldBe("Error_AppleSiliconUnsupported");
    }

    [Fact]
    public void Direct_mac_apple_silicon_vanilla_opens_the_app()
    {
        var cmd = LaunchCommandBuilder.Build(MacGame, LaunchMode.Direct, null, GamePlatform.MacOS, null, AppleSilicon);

        cmd.FileName.ShouldBe("open");
        cmd.Arguments.ShouldBe([MacGame.ExecutablePath]);
        cmd.Environment.ShouldBeNull();
    }

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
