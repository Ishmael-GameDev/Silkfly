using System.IO.Abstractions.TestingHelpers;

namespace Silkfly.Core.Tests;

// "C:\a\b" -> "/a/b" on Linux and macOS
internal static class TestPath
{
    public static string Of(string windowsPath) => MockUnixSupport.Path(windowsPath);
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only.";
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only.";
    }
}
