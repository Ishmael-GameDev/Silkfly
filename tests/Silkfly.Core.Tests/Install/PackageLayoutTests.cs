using Silkfly.Core.Install;
using Silkfly.Core.Models;

namespace Silkfly.Core.Tests.Install;

public sealed class PackageLayoutTests
{
    private static readonly PackageId Id = new("Author", "Mod");
    private const string Fn = "Author-Mod";

    private static Dictionary<string, string> Plan(params string[] entries) =>
        new PackageLayout().Plan(entries, Id).Mappings.ToDictionary(m => m.ZipEntry, m => m.ProfileRelativeTarget);

    [Fact]
    public void Flat_dlls_and_metadata_go_to_the_plugin_folder() =>
        Plan("manifest.json", "icon.png", "README.md", "Silksong.ModMenu.dll", "Silksong.ModMenu.pdb").ShouldBe(new Dictionary<string, string>
        {
            ["manifest.json"] = $"BepInEx/plugins/{Fn}/manifest.json",
            ["icon.png"] = $"BepInEx/plugins/{Fn}/icon.png",
            ["README.md"] = $"BepInEx/plugins/{Fn}/README.md",
            ["Silksong.ModMenu.dll"] = $"BepInEx/plugins/{Fn}/Silksong.ModMenu.dll",
            ["Silksong.ModMenu.pdb"] = $"BepInEx/plugins/{Fn}/Silksong.ModMenu.pdb",
        });

    [Fact]
    public void Standard_folders_are_routed()
    {
        var plan = Plan("plugins/A.dll", "patchers/P.dll", "monomod/Assembly-CSharp.X.mm.dll", "config/a.cfg", "core/Lib.dll");

        plan["plugins/A.dll"].ShouldBe($"BepInEx/plugins/{Fn}/A.dll");
        plan["patchers/P.dll"].ShouldBe($"BepInEx/patchers/{Fn}/P.dll");
        plan["monomod/Assembly-CSharp.X.mm.dll"].ShouldBe($"BepInEx/monomod/{Fn}/Assembly-CSharp.X.mm.dll");
        plan["config/a.cfg"].ShouldBe("BepInEx/config/a.cfg");
        plan["core/Lib.dll"].ShouldBe("BepInEx/core/Lib.dll");
    }

    [Fact]
    public void BepInEx_prefix_is_dropped() =>
        Plan("BepInEx/patchers/X.dll", "BepInEx/core/MonoMod.dll", "BepInEx/plugins/Sub/Y.dll").Values
            .ShouldBe([$"BepInEx/patchers/{Fn}/X.dll", "BepInEx/core/MonoMod.dll", $"BepInEx/plugins/{Fn}/Sub/Y.dll"]);

    [Fact]
    public void Config_files_are_preserved() =>
        new PackageLayout().Plan(["config/a.cfg", "A.dll"], Id).Mappings.Single(m => m.ZipEntry == "config/a.cfg").PreserveIfExists.ShouldBeTrue();

    [Fact]
    public void Root_mm_dll_goes_to_monomod() =>
        Plan("Assembly-CSharp.Mod.mm.dll")["Assembly-CSharp.Mod.mm.dll"].ShouldBe($"BepInEx/monomod/{Fn}/Assembly-CSharp.Mod.mm.dll");

    [Fact]
    public void Other_root_folders_keep_structure() =>
        Plan("Texture/HpBar.png", "Architect/Architect.dll").Values
            .ShouldBe([$"BepInEx/plugins/{Fn}/Texture/HpBar.png", $"BepInEx/plugins/{Fn}/Architect/Architect.dll"]);

    [Fact]
    public void Wrapper_folder_with_manifest_is_stripped() =>
        Plan("Mod-1.0/", "Mod-1.0/manifest.json", "Mod-1.0/plugins/A.dll").Values
            .ShouldBe([$"BepInEx/plugins/{Fn}/manifest.json", $"BepInEx/plugins/{Fn}/A.dll"]);

    [Fact]
    public void Folder_without_manifest_is_not_a_wrapper() =>
        Plan("Lib/A.dll", "Lib/B.dll").Values.ShouldBe([$"BepInEx/plugins/{Fn}/Lib/A.dll", $"BepInEx/plugins/{Fn}/Lib/B.dll"]);

    [Fact]
    public void Junk_and_folders_are_skipped()
    {
        var plan = new PackageLayout().Plan(["plugins/", "__MACOSX/._A.dll", "plugins/.DS_Store", "plugins/A.dll"], Id);

        plan.Mappings.Select(m => m.ZipEntry).ShouldBe(["plugins/A.dll"]);
        plan.Skipped.ShouldContain("__MACOSX/._A.dll");
    }

    [Fact]
    public void Backslashes_are_normalized() =>
        Plan(@"plugins\Sub\A.dll")[@"plugins\Sub\A.dll"].ShouldBe($"BepInEx/plugins/{Fn}/Sub/A.dll");
}
