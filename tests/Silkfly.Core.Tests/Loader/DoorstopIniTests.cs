using Silkfly.Core.Loader;

namespace Silkfly.Core.Tests.Loader;

public sealed class DoorstopIniTests
{
    private const string Ini = """
        # General options for Unity Doorstop
        [General]

        # Enable Doorstop?
        enabled = true

        # Path to the assembly to load and execute
        target_assembly=BepInEx\core\BepInEx.Preloader.dll

        [UnityMono]
        debug_enabled = false
        """;

    [Fact]
    public void Sets_values_and_keeps_comments()
    {
        var result = DoorstopIni.Set(Ini, "General", new Dictionary<string, string>
        {
            ["enabled"] = "false",
            ["target_assembly"] = @"Z:\home\u\p\BepInEx\core\BepInEx.Preloader.dll",
        });

        DoorstopIni.Get(result, "General", "enabled").ShouldBe("false");
        DoorstopIni.Get(result, "General", "target_assembly").ShouldBe(@"Z:\home\u\p\BepInEx\core\BepInEx.Preloader.dll");
        DoorstopIni.Get(result, "UnityMono", "debug_enabled").ShouldBe("false");
        result.ShouldContain("# Enable Doorstop?");
    }

    [Fact]
    public void Adds_missing_key_to_its_section()
    {
        var result = DoorstopIni.Set(Ini, "General", new Dictionary<string, string> { ["ignore_disable_switch"] = "false" });

        DoorstopIni.Get(result, "General", "ignore_disable_switch").ShouldBe("false");
        result.IndexOf("ignore_disable_switch").ShouldBeLessThan(result.IndexOf("[UnityMono]"));
    }

    [Fact]
    public void Does_not_touch_keys_of_other_sections()
    {
        var result = DoorstopIni.Set(Ini, "UnityMono", new Dictionary<string, string> { ["enabled"] = "x" });

        DoorstopIni.Get(result, "General", "enabled").ShouldBe("true");
        DoorstopIni.Get(result, "UnityMono", "enabled").ShouldBe("x");
    }
}
