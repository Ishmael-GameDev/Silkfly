using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Game;

namespace Silkfly.Core.Tests.Game;

public sealed class GameLocatorTests
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeEnvironment _env = new();

    private GameLocator CreateLocator() => new(_fs, _env, NullLogger<GameLocator>.Instance);

    [Fact]
    public void Vdf_parses_libraries_and_manifest()
    {
        var libraries = VdfParser.Parse(Fixture.Read("steam", "libraryfolders.vdf"))["libraryfolders"]!;
        libraries.Children.Values.Select(l => l.GetString("path")).ShouldBe([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"]);
        libraries["1"]!["apps"]!.GetString("1030300").ShouldBe("8000000000");

        VdfParser.Parse(Fixture.Read("steam", "appmanifest_1030300.acf"))["AppState"]!.GetString("installdir")
            .ShouldBe("Hollow Knight Silksong");
    }

    [Fact]
    public void Vdf_rejects_unbalanced_braces() =>
        Should.Throw<FormatException>(() => VdfParser.Parse("\"a\" { \"b\" \"c\""));

    [WindowsFact]
    public void Validate_accepts_folder_or_exe_of_windows_build()
    {
        var game = AddWindowsGame(@"C:\Games\Hollow Knight Silksong");

        var byFolder = CreateLocator().Validate(game);
        var byExe = CreateLocator().Validate(_fs.Path.Combine(game, GameInfo.WindowsExecutable));

        byFolder.IsValid.ShouldBeTrue();
        byFolder.Install!.Platform.ShouldBe(GamePlatform.Windows);
        byExe.Install!.Path.ShouldBe(game);
    }

    [WindowsTheory]
    [InlineData(false, true, "Game_Problem_NoExecutable")]
    [InlineData(true, false, "Game_Problem_NoDataFolder")]
    public void Validate_reports_problems(bool exe, bool data, string problem)
    {
        const string game = @"C:\Games\Broken";
        _fs.AddDirectory(game);
        if (exe)
            _fs.AddFile(_fs.Path.Combine(game, GameInfo.WindowsExecutable), new MockFileData("MZ"));
        if (data)
            _fs.AddDirectory(_fs.Path.Combine(game, GameInfo.DataFolder, "Managed"));

        var result = CreateLocator().Validate(game);

        result.IsValid.ShouldBeFalse();
        result.Problems.ShouldBe([problem]);
    }

    [WindowsFact]
    public void Validate_requires_mono_assembly()
    {
        const string game = @"C:\Games\Il2Cpp";
        _fs.AddFile(_fs.Path.Combine(game, GameInfo.WindowsExecutable), new MockFileData("MZ"));
        _fs.AddDirectory(_fs.Path.Combine(game, GameInfo.DataFolder));

        CreateLocator().Validate(game).Problems.ShouldBe(["Game_Problem_NotMono"]);
    }

    [WindowsFact]
    public void Detects_steam_library_from_libraryfolders()
    {
        _env.Registry[(RegistryRoot.CurrentUser, @"Software\Valve\Steam", "SteamPath")] = "c:/program files (x86)/steam";
        _fs.AddFile(@"c:\program files (x86)\steam\steamapps\libraryfolders.vdf", new MockFileData(Fixture.Read("steam", "libraryfolders.vdf")));
        _fs.AddFile(@"D:\SteamLibrary\steamapps\appmanifest_1030300.acf", new MockFileData(Fixture.Read("steam", "appmanifest_1030300.acf")));
        AddWindowsGame(@"D:\SteamLibrary\steamapps\common\Hollow Knight Silksong");

        var installs = CreateLocator().DetectInstalls();

        installs.Count.ShouldBe(1);
        installs[0].Source.ShouldBe(GameSource.Steam);
        installs[0].Path.ShouldBe(@"D:\SteamLibrary\steamapps\common\Hollow Knight Silksong");
    }

    [WindowsFact]
    public void Detects_gog_install_from_registry_and_deduplicates()
    {
        const string gogKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        _env.SubKeys[(RegistryRoot.LocalMachine, gogKey)] = ["1", "2"];
        _env.Registry[(RegistryRoot.LocalMachine, $@"{gogKey}\1", "gameName")] = "Peglin";
        _env.Registry[(RegistryRoot.LocalMachine, $@"{gogKey}\1", "path")] = @"C:\Games\Peglin";
        _env.Registry[(RegistryRoot.LocalMachine, $@"{gogKey}\2", "gameName")] = "Hollow Knight: Silksong";
        _env.Registry[(RegistryRoot.LocalMachine, $@"{gogKey}\2", "path")] = @"C:\GOG Games\Hollow Knight Silksong";
        _env.Drives = [@"C:\"];
        AddWindowsGame(@"C:\GOG Games\Hollow Knight Silksong");

        var installs = CreateLocator().DetectInstalls();

        installs.Count.ShouldBe(1);
        installs[0].Source.ShouldBe(GameSource.Gog);
    }

    private string AddWindowsGame(string folder)
    {
        _fs.AddFile(_fs.Path.Combine(folder, GameInfo.WindowsExecutable), new MockFileData("MZ"));
        _fs.AddFile(_fs.Path.Combine(folder, GameInfo.DataFolder, "Managed", "Assembly-CSharp.dll"), new MockFileData("MZ"));
        return folder;
    }

    internal sealed class FakeEnvironment : ISystemEnvironment
    {
        public Dictionary<(RegistryRoot, string, string), string> Registry { get; } = new();
        public Dictionary<(RegistryRoot, string), string[]> SubKeys { get; } = new();
        public IReadOnlyList<string> Drives { get; set; } = [];

        public GamePlatform HostPlatform => GamePlatform.Windows;
        public System.Runtime.InteropServices.Architecture HostArchitecture => System.Runtime.InteropServices.Architecture.X64;
        public string HomeDirectory => @"C:\Users\test";
        public IReadOnlyList<string> FixedDriveRoots => Drives;

        public string? GetRegistryValue(RegistryRoot root, string keyPath, string valueName) =>
            Registry.GetValueOrDefault((root, keyPath, valueName));

        public IReadOnlyList<string> GetRegistrySubKeys(RegistryRoot root, string keyPath) =>
            SubKeys.GetValueOrDefault((root, keyPath)) ?? [];
    }
}
