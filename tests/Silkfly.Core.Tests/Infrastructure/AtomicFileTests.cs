using System.IO.Abstractions.TestingHelpers;
using Silkfly.Core.Infrastructure;

namespace Silkfly.Core.Tests.Infrastructure;

public sealed class AtomicFileTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "silkfly-tests", "atomic");
    private static readonly string Target = Path.Combine(Folder, "file.json");

    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Creates_missing_directory_and_leaves_no_temp_file()
    {
        await AtomicFile.WriteAllTextAsync(_fs, Target, "hello");

        _fs.File.ReadAllText(Target).ShouldBe("hello");
        _fs.File.Exists(Target + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task Replaces_existing_content()
    {
        _fs.AddFile(Target, new MockFileData("old"));

        await AtomicFile.WriteAllTextAsync(_fs, Target, "new");

        _fs.File.ReadAllText(Target).ShouldBe("new");
        _fs.File.Exists(Target + ".bak").ShouldBeFalse();
    }

    [Fact]
    public async Task Keeps_backup_of_previous_content_when_asked()
    {
        _fs.AddFile(Target, new MockFileData("old"));

        await AtomicFile.WriteAllTextAsync(_fs, Target, "new", keepBackup: true);

        _fs.File.ReadAllText(Target).ShouldBe("new");
        _fs.File.ReadAllText(Target + ".bak").ShouldBe("old");
    }

    [Fact]
    public async Task Writes_utf8_without_bom()
    {
        await AtomicFile.WriteAllTextAsync(_fs, Target, "Профиль");

        var bytes = _fs.File.ReadAllBytes(Target);
        bytes[0].ShouldNotBe((byte)0xEF);
        _fs.File.ReadAllText(Target).ShouldBe("Профиль");
    }
}
