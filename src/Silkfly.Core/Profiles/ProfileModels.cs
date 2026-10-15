using Semver;
using Silkfly.Core.Models;

namespace Silkfly.Core.Profiles;

public sealed record Profile(
    Guid Id,
    string Name,
    string RootPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastPlayedAt,
    string? IconFile)
{
    public const string LockFileName = "silkfly-profile.json";

    public string BepInExPath => Path.Combine(RootPath, "BepInEx");
    public string PreloaderPath => Path.Combine(BepInExPath, "core", "BepInEx.Preloader.dll");
    public string PluginsPath => Path.Combine(BepInExPath, "plugins");
    public string ConfigPath => Path.Combine(BepInExPath, "config");
    public string LogPath => Path.Combine(BepInExPath, "LogOutput.log");
    public string DisabledPath => Path.Combine(RootPath, "disabled");
    public string StagingPath => Path.Combine(RootPath, ".staging");
    public string LockPath => Path.Combine(RootPath, LockFileName);

    public string? IconPath =>
        IconFile is { Length: > 0 } name && Path.GetFileName(name) == name ? Path.Combine(RootPath, name) : null;
}

public enum ModSource { Thunderstore, ManualZip, ManualDll }

public sealed record InstalledMod(
    PackageId Id,
    SemVersion Version,
    bool Enabled,
    bool Explicit,
    ModSource Source,
    DateTimeOffset InstalledAt,
    IReadOnlyList<string> Files);

public sealed record LoaderRef(string Package, string Version);

public sealed record ProfileLock
{
    public const int CurrentSchema = 1;

    public int Schema { get; init; } = CurrentSchema;
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastPlayedAt { get; init; }
    public string? Icon { get; init; }
    public LoaderRef? Loader { get; init; }
    public IReadOnlyList<InstalledMod> Mods { get; init; } = [];

    public InstalledMod? Find(PackageId id) => Mods.FirstOrDefault(m => m.Id == id);

    public ProfileLock WithMod(InstalledMod mod) =>
        this with { Mods = [.. Mods.Where(m => m.Id != mod.Id), mod] };

    public ProfileLock WithoutMod(PackageId id) =>
        this with { Mods = Mods.Where(m => m.Id != id).ToList() };
}
