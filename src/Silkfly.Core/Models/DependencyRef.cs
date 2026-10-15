using System.Diagnostics.CodeAnalysis;
using Semver;

namespace Silkfly.Core.Models;

public sealed record DependencyRef(PackageId Id, SemVersion Version)
{
    public static DependencyRef Parse(string s) =>
        TryParse(s, out var dep) ? dep : throw new FormatException($"Not a dependency string: '{s}'");

    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out DependencyRef? dep)
    {
        dep = null;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var dash = s.LastIndexOf('-');
        if (dash <= 0 || !Versions.TryParse(s[(dash + 1)..], out var version))
            return false;

        if (!PackageId.TryParseFullName(s[..dash], out var id))
            return false;

        dep = new DependencyRef(id, version);
        return true;
    }

    public bool IsLoaderPack => Id.IsLoaderPack();

    public override string ToString() => $"{Id.FullName}-{Version}";
}

public static class PackageIdExtensions
{
    public static bool IsLoaderPack(this PackageId id) =>
        id.Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase);
}
