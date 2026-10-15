using Silkfly.Core.Models;

namespace Silkfly.Core.Catalog;

public enum SortOrder { Name, Downloads, Rating, Updated, Created }

public readonly record struct ModQueryState(bool Installed, bool Enabled, bool Outdated);

public sealed record CatalogQuery
{
    public string? Text { get; init; }
    public ModFilter Filter { get; init; } = ModFilter.All;
    public IReadOnlySet<string> Categories { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string? Author { get; init; }
    public SortOrder Sort { get; init; } = SortOrder.Name;
    public bool IncludeDeprecated { get; init; }
    public bool IncludeNsfw { get; init; }
    public Func<PackageId, ModQueryState> StateOf { get; init; } = _ => default;
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
    public int WhatsNewDays { get; init; } = 7;

    public bool WhatsNewIncludesNew { get; init; } = true;

    public bool WhatsNewIncludesUpdated { get; init; }
}

public static class CatalogSearch
{
    public static IReadOnlyList<Package> Query(IEnumerable<Package> packages, CatalogQuery query)
    {
        var words = (query.Text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var newSince = query.Now - TimeSpan.FromDays(query.WhatsNewDays);
        var categories = new HashSet<string>(query.Categories, StringComparer.OrdinalIgnoreCase);

        var filtered = packages.Where(p =>
        {
            var state = query.StateOf(p.Id);

            // installed mods are always listed
            if (!state.Installed)
            {
                if (p.IsDeprecated && !query.IncludeDeprecated)
                    return false;
                if (p.HasNsfwContent && !query.IncludeNsfw)
                    return false;
            }

            var passesFilter = query.Filter switch
            {
                ModFilter.Installed => state.Installed,
                ModFilter.Enabled => state.Installed && state.Enabled,
                ModFilter.Outdated => state.Installed && state.Outdated,
                ModFilter.WhatsNew =>
                    (query.WhatsNewIncludesNew && p.DateCreated >= newSince) ||
                    (query.WhatsNewIncludesUpdated && p.DateCreated < newSince && p.DateUpdated >= newSince),
                _ => true,
            };
            if (!passesFilter)
                return false;

            if (categories.Count > 0 && !p.Categories.Any(categories.Contains))
                return false;

            if (!string.IsNullOrEmpty(query.Author) && !string.Equals(p.Owner, query.Author, StringComparison.OrdinalIgnoreCase))
                return false;

            return words.Length == 0 || words.All(w => Matches(p, w));
        });

        return Sort(filtered, query.Sort).ToList();
    }

    private static bool Matches(Package p, string word) =>
        p.DisplayName.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        p.Id.Name.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        p.Owner.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        p.Latest.Description.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        p.Categories.Any(c => c.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Package> Sort(IEnumerable<Package> packages, SortOrder sort)
    {
        var byName = StringComparer.OrdinalIgnoreCase;
        return sort switch
        {
            SortOrder.Downloads => packages.OrderByDescending(p => p.TotalDownloads).ThenBy(p => p.DisplayName, byName),
            SortOrder.Rating => packages.OrderByDescending(p => p.RatingScore).ThenBy(p => p.DisplayName, byName),
            SortOrder.Updated => packages.OrderByDescending(p => p.DateUpdated),
            SortOrder.Created => packages.OrderByDescending(p => p.DateCreated),
            _ => packages.OrderBy(p => p.DisplayName, byName).ThenBy(p => p.Owner, byName),
        };
    }
}
