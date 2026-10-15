using Silkfly.Core.Install;
using Silkfly.Core.Models;

namespace Silkfly.Core.Tests.Install;

public sealed class UnmanagedMatcherTests
{
    private static readonly Package[] Catalog =
    [
        TestCatalog.Package("Someone-ModMenu", "0.8.0"),
        TestCatalog.Package("Other-ModMenuExtras", "1.0.0"),
        TestCatalog.Package("Someone-Architect", "3.41.0"),
        TestCatalog.Package("Tiny-UI", "1.0.0"),
    ];

    private static IEnumerable<string> Suggest(string relativePath, bool isDirectory = false) =>
        UnmanagedMatcher.Suggest(new UnmanagedItem(relativePath, isDirectory, LooksDisabled: false), Catalog).Select(p => p.Id.FullName);

    [Fact]
    public void Author_name_folder_matches_exactly() =>
        Suggest("BepInEx/plugins/Someone-Architect", isDirectory: true).ShouldBe(["Someone-Architect"]);

    [Fact]
    public void Dll_name_matches_package_name_first() =>
        Suggest("BepInEx/plugins/Mod_Menu.dll").ShouldBe(["Someone-ModMenu", "Other-ModMenuExtras"]);

    [Theory]
    [InlineData("BepInEx/plugins/Architect-3.41.0", true)]
    [InlineData("BepInEx/plugins/Architect_v3.41.dll.disabled", false)]
    public void Versions_and_disabled_suffixes_are_ignored(string path, bool isDirectory) =>
        Suggest(path, isDirectory).ShouldBe(["Someone-Architect"]);

    [Fact]
    public void Short_names_are_not_guessed() =>
        Suggest("BepInEx/plugins/UI.dll").ShouldBeEmpty();
}
