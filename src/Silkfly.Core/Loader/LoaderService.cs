using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Semver;
using Silkfly.Core.Catalog;
using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;

namespace Silkfly.Core.Loader;

public sealed record LoaderStatus(
    bool IsInstalled,
    SemVersion? InstalledPackVersion,
    SemVersion? LatestPackVersion,
    bool DoorstopConfigPresent,
    bool ForeignLoaderDetected,
    IReadOnlyList<string> Problems)
{
    public bool UpdateAvailable =>
        IsInstalled && InstalledPackVersion is not null && LatestPackVersion is not null &&
        LatestPackVersion.IsNewerThan(InstalledPackVersion);
}

public interface ILoaderService
{
    Task<LoaderStatus> GetStatusAsync(GameInstall game, CancellationToken ct);

    Task InstallOrUpdateAsync(GameInstall game, IProgress<double>? progress, CancellationToken ct);

    Task RemoveAsync(GameInstall game, CancellationToken ct);

    Task<bool> EnsureProfileCoreAsync(string profileRoot, CancellationToken ct);

    Task SetDoorstopConfigAsync(GameInstall game, bool enabled, string? targetAssembly, CancellationToken ct);
}

public sealed class LoaderService(
    IFileSystem fs,
    IAppPaths paths,
    ICatalogService catalog,
    IPackageCache packages,
    TimeProvider time,
    ILogger<LoaderService> logger) : ILoaderService
{
    public const string ManifestFileName = ".silkfly-loader.json";
    public const string BackupFolderName = ".silkfly-backup";
    private const string CompleteMarker = ".complete";

    private static readonly string[] WindowsFiles = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version", "steam_appid.txt"];
    private static readonly string[] LinuxFiles = ["run_bepinex.sh", "libdoorstop.so", ".doorstop_version", "steam_appid.txt"];
    private static readonly string[] MacFiles = ["run_bepinex.sh", "libdoorstop.dylib", ".doorstop_version", "steam_appid.txt"];

    public static IReadOnlyList<string> FilesFor(GamePlatform platform) => platform switch
    {
        GamePlatform.Windows => WindowsFiles,
        GamePlatform.MacOS => MacFiles,
        _ => LinuxFiles,
    };

    public Task<LoaderStatus> GetStatusAsync(GameInstall game, CancellationToken ct)
    {
        var problems = new List<string>();
        var manifest = ReadManifest(game);
        var latest = catalog.Find(CatalogRules.LoaderPackage)?.Latest.Version;
        var iniPresent = fs.File.Exists(fs.Path.Combine(game.Path, "doorstop_config.ini"));

        SemVersion? installed = null;
        if (manifest is not null)
        {
            Versions.TryParse(manifest.PackVersion, out installed);
            if (manifest.Files.Any(f => !fs.File.Exists(fs.Path.Combine(game.Path, f))))
                problems.Add("Loader_Problem_FilesMissing");
        }

        var foreign = manifest is null && FilesFor(game.Platform)
            .Where(f => f is "winhttp.dll" or "run_bepinex.sh" or "doorstop_config.ini")
            .Any(f => fs.File.Exists(fs.Path.Combine(game.Path, f)));

        return Task.FromResult(new LoaderStatus(manifest is not null && problems.Count == 0, installed, latest, iniPresent, foreign, problems));
    }

    public async Task InstallOrUpdateAsync(GameInstall game, IProgress<double>? progress, CancellationToken ct)
    {
        var package = catalog.Find(CatalogRules.LoaderPackage) ?? throw new SilkflyException("Error_LoaderNotInCatalog");
        var version = package.Latest;
        logger.LogInformation("Installing loader {Version} into {Game}", version.Version, game.Path);

        var zip = await packages.GetOrDownloadAsync(
            version,
            version.FileSize > 0 ? new Progress<long>(b => progress?.Report(0.8 * b / version.FileSize)) : null,
            ct).ConfigureAwait(false);

        var reference = await UnpackReferenceAsync(zip, version.Version, ct).ConfigureAwait(false);
        progress?.Report(0.85);

        var previous = ReadManifest(game);
        var manifest = new LoaderManifest
        {
            PackVersion = version.Version.ToString(),
            InstalledAt = time.GetUtcNow(),
            Backups = new Dictionary<string, string>(previous?.Backups ?? [], StringComparer.OrdinalIgnoreCase),
        };

        try
        {
            foreach (var name in FilesFor(game.Platform))
            {
                var source = fs.Path.Combine(reference, name);
                if (!fs.File.Exists(source))
                {
                    logger.LogWarning("Loader pack has no {File}", name);
                    continue;
                }

                var target = fs.Path.Combine(game.Path, name);
                var ours = previous?.Files.Contains(name, StringComparer.OrdinalIgnoreCase) == true;
                if (!ours && fs.File.Exists(target))
                {
                    // identical file from another tool: leave it, don't claim it
                    if (!IsLink(target) && SameContent(source, target) && name != "doorstop_config.ini")
                        continue;

                    // move, not copy: other managers' links stay restorable
                    var backupDir = fs.Path.Combine(game.Path, BackupFolderName);
                    fs.Directory.CreateDirectory(backupDir);
                    var backup = fs.Path.Combine(backupDir, name);
                    if (!fs.File.Exists(backup))
                    {
                        fs.File.Move(target, backup);
                        manifest.Backups[name] = $"{BackupFolderName}/{name}";
                    }
                    else
                    {
                        fs.File.Delete(target);
                    }
                }

                fs.File.Copy(source, target, overwrite: true);
                manifest.Files.Add(name);

                if (name == "run_bepinex.sh" && !OperatingSystem.IsWindows())
                    MakeExecutable(target);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InsufficientPermissionsException(game.Path, ex);
        }

        // keep the game vanilla outside Silkfly
        var ini = fs.Path.Combine(game.Path, "doorstop_config.ini");
        if (game.Platform == GamePlatform.Windows && fs.File.Exists(ini))
            fs.File.WriteAllText(ini, DoorstopIni.Set(fs.File.ReadAllText(ini), "General", new Dictionary<string, string> { ["enabled"] = "false" }));

        await WriteManifestAsync(game, manifest, ct).ConfigureAwait(false);
        progress?.Report(0.9);

        foreach (var profileRoot in ProfileFolders())
            await EnsureProfileCoreAsync(profileRoot, ct).ConfigureAwait(false);

        progress?.Report(1);
    }

    public async Task RemoveAsync(GameInstall game, CancellationToken ct)
    {
        var manifest = ReadManifest(game);
        if (manifest is null)
            return;

        try
        {
            foreach (var name in manifest.Files)
            {
                var target = fs.Path.Combine(game.Path, name);
                if (fs.File.Exists(target))
                    fs.File.Delete(target);
            }

            foreach (var (name, relative) in manifest.Backups)
            {
                var backup = fs.Path.Combine(game.Path, relative);
                var target = fs.Path.Combine(game.Path, name);
                if (fs.File.Exists(backup) && !fs.File.Exists(target))
                    fs.File.Move(backup, target);
            }

            var backupDir = fs.Path.Combine(game.Path, BackupFolderName);
            if (fs.Directory.Exists(backupDir) && !fs.Directory.EnumerateFileSystemEntries(backupDir).Any())
                fs.Directory.Delete(backupDir);

            fs.File.Delete(fs.Path.Combine(game.Path, ManifestFileName));
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InsufficientPermissionsException(game.Path, ex);
        }

        logger.LogInformation("Removed loader from {Game}", game.Path);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<bool> EnsureProfileCoreAsync(string profileRoot, CancellationToken ct)
    {
        var reference = LatestReference();
        if (reference is null)
            return false;

        var sourceCore = fs.Path.Combine(reference, "BepInEx", "core");
        var targetCore = fs.Path.Combine(profileRoot, "BepInEx", "core");
        fs.Directory.CreateDirectory(targetCore);
        foreach (var file in fs.Directory.EnumerateFiles(sourceCore, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = fs.Path.GetRelativePath(sourceCore, file);
            var target = fs.Path.Combine(targetCore, relative);
            fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(target)!);
            fs.File.Copy(file, target, overwrite: true);
        }

        var cfg = fs.Path.Combine(profileRoot, "BepInEx", "config", "BepInEx.cfg");
        var sourceCfg = fs.Path.Combine(reference, "BepInEx", "config", "BepInEx.cfg");
        if (!fs.File.Exists(cfg) && fs.File.Exists(sourceCfg))
        {
            fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(cfg)!);
            fs.File.Copy(sourceCfg, cfg);
        }

        foreach (var folder in new[] { "plugins", "patchers", "config" })
            fs.Directory.CreateDirectory(fs.Path.Combine(profileRoot, "BepInEx", folder));

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    public async Task SetDoorstopConfigAsync(GameInstall game, bool enabled, string? targetAssembly, CancellationToken ct)
    {
        var ini = fs.Path.Combine(game.Path, "doorstop_config.ini");
        if (!fs.File.Exists(ini))
            throw new LoaderMissingException();

        var values = new Dictionary<string, string> { ["enabled"] = enabled ? "true" : "false" };
        if (targetAssembly is not null)
            values["target_assembly"] = targetAssembly;

        var text = DoorstopIni.Set(await fs.File.ReadAllTextAsync(ini, ct).ConfigureAwait(false), "General", values);
        await AtomicFile.WriteAllTextAsync(fs, ini, text, ct: ct).ConfigureAwait(false);
    }

    private async Task<string> UnpackReferenceAsync(string zipPath, SemVersion version, CancellationToken ct)
    {
        var target = fs.Path.Combine(paths.LoaderCacheRoot, version.ToString());
        if (fs.File.Exists(fs.Path.Combine(target, CompleteMarker)))
            return target;

        if (fs.Directory.Exists(target))
            fs.Directory.Delete(target, recursive: true);
        fs.Directory.CreateDirectory(target);

        using (var zip = SafeZip.OpenRead(fs, zipPath))
        {
            SafeZip.Validate(zip);
            foreach (var entry in zip.Entries)
            {
                var name = SafeZip.Normalize(entry.FullName);
                if (!name.StartsWith("BepInExPack/", StringComparison.OrdinalIgnoreCase) || name.EndsWith('/'))
                    continue;

                var relative = name["BepInExPack/".Length..];
                await SafeZip.ExtractEntryAsync(fs, entry, SafeZip.ResolveInside(fs, target, relative), ct).ConfigureAwait(false);
            }
        }

        if (!fs.File.Exists(fs.Path.Combine(target, "BepInEx", "core", "BepInEx.Preloader.dll")))
            throw new SilkflyException("Error_LoaderPackInvalid");

        await fs.File.WriteAllTextAsync(fs.Path.Combine(target, CompleteMarker), version.ToString(), ct).ConfigureAwait(false);
        return target;
    }

    private string? LatestReference()
    {
        if (!fs.Directory.Exists(paths.LoaderCacheRoot))
            return null;

        return fs.Directory.GetDirectories(paths.LoaderCacheRoot)
            .Where(d => fs.File.Exists(fs.Path.Combine(d, CompleteMarker)))
            .Select(d => (Dir: d, Ok: Versions.TryParse(fs.Path.GetFileName(d), out var v), Version: v))
            .Where(x => x.Ok)
            .OrderByDescending(x => x.Version!, Comparer<SemVersion>.Create(Versions.Compare))
            .Select(x => x.Dir)
            .FirstOrDefault();
    }

    private IEnumerable<string> ProfileFolders() =>
        fs.Directory.Exists(paths.ProfilesRoot)
            ? fs.Directory.GetDirectories(paths.ProfilesRoot).Where(d => fs.File.Exists(fs.Path.Combine(d, Profile.LockFileName)))
            : [];

    private LoaderManifest? ReadManifest(GameInstall game)
    {
        var path = fs.Path.Combine(game.Path, ManifestFileName);
        if (!fs.File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize(fs.File.ReadAllText(path), LoaderJsonContext.Default.LoaderManifest);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Loader manifest {Path} is corrupt", path);
            return null;
        }
    }

    private Task WriteManifestAsync(GameInstall game, LoaderManifest manifest, CancellationToken ct) =>
        AtomicFile.WriteAllTextAsync(
            fs, fs.Path.Combine(game.Path, ManifestFileName),
            JsonSerializer.Serialize(manifest, LoaderJsonContext.Default.LoaderManifest), ct: ct);

    private bool IsLink(string path) => fs.FileInfo.New(path).LinkTarget is not null;

    private bool SameContent(string a, string b)
    {
        var fa = fs.FileInfo.New(a);
        var fb = fs.FileInfo.New(b);
        if (fa.Length != fb.Length)
            return false;
        return fs.File.ReadAllBytes(a).AsSpan().SequenceEqual(fs.File.ReadAllBytes(b));
    }

    private void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        var mode = fs.File.GetUnixFileMode(path);
        fs.File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}

public sealed class LoaderManifest
{
    public int Schema { get; set; } = 1;
    public string PackVersion { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
    public List<string> Files { get; set; } = [];
    public Dictionary<string, string> Backups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(LoaderManifest))]
internal sealed partial class LoaderJsonContext : JsonSerializerContext;
