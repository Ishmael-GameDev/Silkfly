using System.Diagnostics.CodeAnalysis;
using Semver;

namespace Silkfly.Core.Models;

public static class Versions
{
    public static readonly SemVersion Zero = new(0, 0, 0);

    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out SemVersion? version) =>
        SemVersion.TryParse(s, SemVersionStyles.Strict, out version);

    public static SemVersion Parse(string s) => SemVersion.Parse(s, SemVersionStyles.Strict);

    public static int Compare(SemVersion? a, SemVersion? b) => SemVersion.ComparePrecedence(a, b);

    public static bool IsNewerThan(this SemVersion a, SemVersion b) => Compare(a, b) > 0;

    public static bool IsAtLeast(this SemVersion a, SemVersion b) => Compare(a, b) >= 0;

    public static bool SameAs(this SemVersion a, SemVersion b) => Compare(a, b) == 0;
}
