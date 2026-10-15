using System.IO.Abstractions.TestingHelpers;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;

namespace Silkfly.Core.Tests.Install;

public sealed class SafeZipTests
{
    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("plugins/../../evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData(@"plugins\..\..\evil.dll")]
    [InlineData("plugins/CON.dll")]
    [InlineData("nul")]
    [InlineData("a/b:stream")]
    public void Unsafe_paths_are_detected(string entry) => SafeZip.IsUnsafePath(entry).ShouldBeTrue();

    [Theory]
    [InlineData("plugins/Mod.dll")]
    [InlineData("BepInEx/config/a.cfg")]
    [InlineData("manifest.json")]
    [InlineData("assets/console.png")]
    public void Normal_paths_are_allowed(string entry) => SafeZip.IsUnsafePath(entry).ShouldBeFalse();

    [Fact]
    public void Archive_with_traversal_entry_is_rejected()
    {
        var fs = new MockFileSystem();
        FakePackageCache.WriteZip(fs, TestPath.Of(@"C:\t\bad.zip"), new Dictionary<string, string> { ["ok.dll"] = "x", ["../../evil.dll"] = "y" });

        using var zip = SafeZip.OpenRead(fs, TestPath.Of(@"C:\t\bad.zip"));
        Should.Throw<UnsafeArchiveException>(() => SafeZip.Validate(zip));
    }

    [Fact]
    public void Resolve_inside_rejects_escape()
    {
        var fs = new MockFileSystem();
        SafeZip.ResolveInside(fs, TestPath.Of(@"C:\root"), "a/b.dll").ShouldBe(TestPath.Of(@"C:\root\a\b.dll"));
        Should.Throw<UnsafeArchiveException>(() => SafeZip.ResolveInside(fs, TestPath.Of(@"C:\root"), "../x.dll"));
    }
}
