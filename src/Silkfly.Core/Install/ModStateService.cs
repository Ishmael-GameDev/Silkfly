using System.IO.Abstractions;
using System.Text.Json;
using Silkfly.Core.Catalog;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;

namespace Silkfly.Core.Install;

public enum ModStateKind { NotInstalled, Installed, Disabled, Outdated, Busy, Failed }

public sealed class ProfileModsSnapshot
{
    public static readonly ProfileModsSnapshot Empty = new(null, new Dictionary<PackageId, InstalledMod>(), new HashSet<PackageId>(), [], []);

    public ProfileModsSnapshot(
        Profile? profile,
        IReadOnlyDictionary<PackageId, InstalledMod> installed,
        IReadOnlySet<PackageId> broken,
        IReadOnlyList<UnmanagedItem> unmanaged,
        IReadOnlyList<Package> localPackages)
    {
        Profile = profile;
        Installed = installed;
        Broken = broken;
        Unmanaged = unmanaged;
        LocalPackages = localPackages;
    }

    public Profile? Profile { get; }

    public IReadOnlyDictionary<PackageId, InstalledMod> Installed { get; }

    public IReadOnlySet<PackageId> Broken { get; }

    public IReadOnlyList<UnmanagedItem> Unmanaged { get; }

    public IReadOnlyList<Package> LocalPackages { get; }

    public InstalledMod? Find(PackageId id) => Installed.GetValueOrDefault(id);
}

public interface IModStateService
{
    ProfileModsSnapshot Load(Profile profile);

    bool IsOutdated(InstalledMod mod);
}

public sealed class ModStateService(
    IFileSystem fs,
    IProfileService profiles,
    IModInstaller installer,
    ICatalogService catalog) : IModStateService
{
    public ProfileModsSnapshot Load(Profile profile)
    {
        var lockFile = profiles.GetLock(profile);
        var installed = lockFile.Mods.ToDictionary(m => m.Id);
        var broken = lockFile.Mods.Where(m => !installer.IsIntact(profile, m)).Select(m => m.Id).ToHashSet();
        var local = lockFile.Mods
            .Where(m => catalog.Find(m.Id) is null)
            .Select(m => LocalPackage(profile, m))
            .ToList();

        return new ProfileModsSnapshot(profile, installed, broken, installer.ScanUnmanaged(profile), local);
    }

    public bool IsOutdated(InstalledMod mod) =>
        mod.Source != ModSource.ManualDll &&
        catalog.Find(mod.Id) is { } package &&
        package.Latest.Version.IsNewerThan(mod.Version);

    private Package LocalPackage(Profile profile, InstalledMod mod)
    {
        var description = string.Empty;
        Uri? website = null;
        var manifestFile = mod.Files.FirstOrDefault(f => f.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestFile is not null)
        {
            var physical = fs.Path.Combine(profile.RootPath, (mod.Enabled ? manifestFile : "disabled/" + manifestFile).Replace('/', fs.Path.DirectorySeparatorChar));
            try
            {
                if (fs.File.Exists(physical))
                {
                    using var doc = JsonDocument.Parse(fs.File.ReadAllText(physical));
                    if (doc.RootElement.TryGetProperty("description", out var d))
                        description = d.GetString() ?? string.Empty;
                    if (doc.RootElement.TryGetProperty("website_url", out var w) && Uri.TryCreate(w.GetString(), UriKind.Absolute, out var uri))
                        website = uri;
                }
            }
            catch (JsonException)
            {
            }
        }

        var url = new Uri($"https://thunderstore.io/c/{CatalogRules.Community}/p/{Uri.EscapeDataString(mod.Id.Namespace)}/{Uri.EscapeDataString(mod.Id.Name)}/");
        var version = new PackageVersion(mod.Id, mod.Version, description, null, url, 0, mod.InstalledAt, website, [], 0, true);
        return new Package(mod.Id, mod.Id.Namespace, url, null, mod.InstalledAt, mod.InstalledAt, 0, false, false, false,
            mod.Source == ModSource.Thunderstore ? [] : ["Manual"], [version]);
    }
}
