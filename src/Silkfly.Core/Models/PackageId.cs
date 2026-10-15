using System.Diagnostics.CodeAnalysis;

namespace Silkfly.Core.Models;

public readonly record struct PackageId(string Namespace, string Name)
{
    public string FullName => $"{Namespace}-{Name}";

    public string DisplayName => Name.Replace('_', ' ');

    public static PackageId ParseFullName(string fullName) =>
        TryParseFullName(fullName, out var id)
            ? id
            : throw new FormatException($"Not a package name: '{fullName}'");

    public static bool TryParseFullName([NotNullWhen(true)] string? s, out PackageId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var dash = s.LastIndexOf('-');
        if (dash <= 0 || dash == s.Length - 1)
            return false;

        id = new PackageId(s[..dash], s[(dash + 1)..]);
        return true;
    }

    public bool Equals(PackageId other) =>
        string.Equals(Namespace, other.Namespace, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Namespace ?? string.Empty),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Name ?? string.Empty));

    public override string ToString() => FullName;
}
