using Silkfly.Core.Models;

namespace Silkfly.Core.Install;

public sealed record LayoutMapping(string ZipEntry, string ProfileRelativeTarget, bool PreserveIfExists);

public sealed record LayoutPlan(IReadOnlyList<LayoutMapping> Mappings, IReadOnlyList<string> Skipped);

public interface IPackageLayout
{
    LayoutPlan Plan(IReadOnlyList<string> zipEntries, PackageId id);
}

public sealed class PackageLayout : IPackageLayout
{
    private static readonly string[] MetaFiles = ["manifest.json", "icon.png", "readme.md", "changelog.md"];

    public LayoutPlan Plan(IReadOnlyList<string> zipEntries, PackageId id)
    {
        var fn = id.FullName;
        var mappings = new List<LayoutMapping>();
        var skipped = new List<string>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var files = zipEntries
            .Select(e => (Entry: e, Path: SafeZip.Normalize(e)))
            .Where(e => e.Path.Length > 0)
            .ToList();

        var wrapper = FindWrapper(files.Select(f => f.Path).ToList());

        foreach (var (entry, rawPath) in files)
        {
            var path = wrapper is not null ? rawPath[(wrapper.Length + 1)..] : rawPath;

            if (path.Length == 0 || path.EndsWith('/') ||
                path.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
                path.Split('/')[^1].Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
            {
                if (!path.EndsWith('/') && path.Length > 0)
                    skipped.Add(entry);
                continue;
            }

            var target = Map(path, fn, out var preserve);
            if (target is null || !targets.Add(target))
            {
                skipped.Add(entry);
                continue;
            }

            mappings.Add(new LayoutMapping(entry, target, preserve));
        }

        return new LayoutPlan(mappings, skipped);
    }

    private static string? Map(string path, string fn, out bool preserve)
    {
        preserve = false;
        var segments = path.Split('/');

        if (segments.Length == 1 && IsMetaFile(segments[0]))
            return $"BepInEx/plugins/{fn}/{segments[0]}";

        if (segments.Length > 1 && segments[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
            segments = segments[1..];

        if (segments.Length > 1)
        {
            var rest = string.Join('/', segments[1..]);
            switch (segments[0].ToLowerInvariant())
            {
                case "plugins":
                    return $"BepInEx/plugins/{fn}/{rest}";
                case "patchers":
                    return $"BepInEx/patchers/{fn}/{rest}";
                case "monomod":
                    return $"BepInEx/monomod/{fn}/{rest}";
                case "config":
                    preserve = true;
                    return $"BepInEx/config/{rest}";
                case "core":
                    // core/ libraries (MonoDetour etc.) go to BepInEx/core; files we don't own are never overwritten
                    return $"BepInEx/core/{rest}";
            }
        }

        var name = segments[^1];
        if (segments.Length == 1 && name.EndsWith(".mm.dll", StringComparison.OrdinalIgnoreCase))
            return $"BepInEx/monomod/{fn}/{name}";

        return $"BepInEx/plugins/{fn}/{string.Join('/', segments)}";
    }

    private static bool IsMetaFile(string name) =>
        MetaFiles.Contains(name, StringComparer.OrdinalIgnoreCase) ||
        name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase);

    private static string? FindWrapper(IReadOnlyList<string> paths)
    {
        var meaningful = paths.Where(p => !p.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (meaningful.Count == 0 || meaningful.Any(p => p.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
            return null;

        var first = meaningful[0].Split('/')[0];
        var allInside = meaningful.All(p => p.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase) || p.Equals(first + "/", StringComparison.OrdinalIgnoreCase));
        var hasManifest = meaningful.Any(p => p.Equals($"{first}/manifest.json", StringComparison.OrdinalIgnoreCase));
        return allInside && hasManifest ? first : null;
    }
}
