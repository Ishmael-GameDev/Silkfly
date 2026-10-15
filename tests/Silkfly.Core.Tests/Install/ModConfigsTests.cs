using System.IO.Abstractions.TestingHelpers;
using Semver;
using Silkfly.Core.Install;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;

namespace Silkfly.Core.Tests.Install;

public sealed class ModConfigsTests
{
    private readonly MockFileSystem _fs = new();
    private readonly Profile _profile = new(Guid.NewGuid(), "Test", TestPath.Of(@"C:\Silkfly\profiles\p"), DateTimeOffset.UtcNow, null, null);

    private string Config(string name) => Path.Combine(_profile.ConfigPath, name);

    private static InstalledMod Mod(string fullName, params string[] files) =>
        new(PackageId.ParseFullName(fullName), SemVersion.Parse("1.0.0"), true, true, ModSource.Thunderstore, DateTimeOffset.UtcNow, files);

    [Fact]
    public void Finds_config_named_after_the_plugin_guid()
    {
        _fs.AddFile(Config("io.github.someone.modmenu.cfg"), new MockFileData(""));
        _fs.AddFile(Config("BepInEx.cfg"), new MockFileData(""));
        _fs.AddFile(Config("com.other.debugmod.cfg"), new MockFileData(""));

        ModConfigs.Find(_fs, _profile, Mod("Someone-Mod_Menu")).ShouldBe([Config("io.github.someone.modmenu.cfg")]);
    }

    [Fact]
    public void Shipped_config_files_come_first_and_are_not_repeated()
    {
        _fs.AddFile(Config(Path.Combine("Lib", "settings.json")), new MockFileData(""));
        _fs.AddFile(Config("author.wavlib.cfg"), new MockFileData(""));

        var found = ModConfigs.Find(_fs, _profile,
            Mod("Author-WavLib", "BepInEx/plugins/Author-WavLib/WavLib.dll", "BepInEx/config/Lib/settings.json", "BepInEx/config/author.wavlib.cfg"));

        found.ShouldBe([Config(Path.Combine("Lib", "settings.json")), Config("author.wavlib.cfg")]);
    }

    [Fact]
    public void Short_names_do_not_match_by_name()
    {
        _fs.AddFile(Config("com.someone.fixedui.cfg"), new MockFileData(""));

        ModConfigs.Find(_fs, _profile, Mod("Someone-UI")).ShouldBeEmpty();
    }

    [Fact]
    public void Missing_config_folder_means_none() =>
        ModConfigs.Find(_fs, _profile, Mod("Someone-ModMenu")).ShouldBeEmpty();
}
