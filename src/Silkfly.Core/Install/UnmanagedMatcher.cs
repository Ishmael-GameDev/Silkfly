using System.Text.RegularExpressions;
using Silkfly.Core.Models;

namespace Silkfly.Core.Install;

public static partial class UnmanagedMatcher
{
    public const int MaxSuggestions = 5;

    private const int MinKeyLength = 4;

    public static IReadOnlyList<Package> Suggest(UnmanagedItem item, IEnumerable<Package> packages)
    {
        var stem = Stem(item);
        var all = packages as IReadOnlyCollection<Package> ?? packages.ToList();

        if (PackageId.TryParseFullName(stem, out var exactId) && all.FirstOrDefault(p => p.Id == exactId) is { } exact)
            return [exact];

        var key = Normalize(TrailingVersion().Replace(stem, string.Empty));
        if (key.Length < MinKeyLength)
            return [];

        return all
            .Select(p => (Package: p, Name: Normalize(p.Id.Name)))
            .Select(x => (x.Package, Score: x.Name == key ? 0 : x.Name.Length >= MinKeyLength && (x.Name.Contains(key) || key.Contains(x.Name)) ? 1 : -1))
            .Where(x => x.Score >= 0)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Package.IsDeprecated)
            .ThenByDescending(x => x.Package.TotalDownloads)
            .Take(MaxSuggestions)
            .Select(x => x.Package)
            .ToList();
    }

    private static string Stem(UnmanagedItem item)
    {
        var name = item.Name;
        foreach (var suffix in new[] { ".disabled", ".old", ".bak" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }

        return !item.IsDirectory && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"[\s_\-.]*v?\d+(\.\d+)+$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVersion();
}
