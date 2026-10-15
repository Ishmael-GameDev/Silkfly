using System.IO.Abstractions;
using Silkfly.Core.Profiles;

namespace Silkfly.Core.Install;

public static class ModConfigs
{
    private const int MinNameLength = 4;

    private const string ConfigPrefix = "BepInEx/config/";

    public static IReadOnlyList<string> Find(IFileSystem fs, Profile profile, InstalledMod mod)
    {
        if (!fs.Directory.Exists(profile.ConfigPath))
            return [];

        var found = new List<string>();
        foreach (var file in mod.Files.Where(f => f.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var path = fs.Path.Combine(profile.RootPath, file.Replace('/', fs.Path.DirectorySeparatorChar));
            if (fs.File.Exists(path))
                found.Add(path);
        }

        var key = Normalize(mod.Id.Name);
        if (key.Length >= MinNameLength)
        {
            var byName = fs.Directory
                .EnumerateFiles(profile.ConfigPath, "*.cfg", SearchOption.AllDirectories)
                .Where(path => Normalize(fs.Path.GetFileNameWithoutExtension(path)).Contains(key, StringComparison.Ordinal))
                .Order(StringComparer.OrdinalIgnoreCase);
            found.AddRange(byName.Where(path => !found.Contains(path, StringComparer.OrdinalIgnoreCase)));
        }

        return found;
    }

    private static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
