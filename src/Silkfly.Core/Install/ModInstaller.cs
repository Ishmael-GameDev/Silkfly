using System.IO.Abstractions;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Semver;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;

namespace Silkfly.Core.Install;

public sealed record UnmanagedItem(string RelativePath, bool IsDirectory, bool LooksDisabled)
{
    public string Name => RelativePath.Split('/')[^1];
}

public sealed record ManualPackageInfo(PackageId Id, SemVersion Version, ModSource Source);

public interface IModInstaller
{
    Task<InstalledMod> InstallArchiveAsync(
        Profile profile, string zipPath, PackageId id, SemVersion version, ModSource source, bool isExplicit, CancellationToken ct);

    Task UninstallAsync(Profile profile, PackageId id, bool keepConfigs, CancellationToken ct);

    Task SetEnabledAsync(Profile profile, PackageId id, bool enabled, CancellationToken ct);

    ManualPackageInfo IdentifyManual(string filePath, Func<string, SemVersion, PackageId?> findInCatalog);

    Task<InstalledMod> InstallManualAsync(Profile profile, string filePath, ManualPackageInfo info, CancellationToken ct);

    IReadOnlyList<UnmanagedItem> ScanUnmanaged(Profile profile);

    Task RemoveUnmanagedAsync(Profile profile, UnmanagedItem item, CancellationToken ct);

    Task SetUnmanagedEnabledAsync(Profile profile, UnmanagedItem item, bool enabled, CancellationToken ct);

    bool IsIntact(Profile profile, InstalledMod mod);

    Task RecoverAsync(Profile profile, CancellationToken ct);
}

public sealed class ModInstaller(
    IFileSystem fs,
    IProfileService profiles,
    IPackageLayout layout,
    TimeProvider time,
    ILogger<ModInstaller> logger) : IModInstaller
{
    private const string DisabledPrefix = "disabled/";
    private const string UnmanagedDisabledFolder = "disabled/unmanaged";
    private const string PluginsFolder = "BepInEx/plugins";

    public async Task<InstalledMod> InstallArchiveAsync(
        Profile profile, string zipPath, PackageId id, SemVersion version, ModSource source, bool isExplicit, CancellationToken ct)
    {
        var lockFile = profiles.GetLock(profile);
        var existing = lockFile.Find(id);
        var enabled = existing?.Enabled ?? true;
        var opId = Guid.NewGuid();
        var staging = StagingFor(profile, opId);

        // extract to staging first
        LayoutPlan plan;
        try
        {
            using var zip = SafeZip.OpenRead(fs, zipPath);
            SafeZip.Validate(zip);
            plan = layout.Plan(zip.Entries.Select(e => e.FullName).ToList(), id);
            foreach (var skipped in plan.Skipped)
                logger.LogDebug("{Package}: skipped archive entry {Entry}", id, skipped);

            var entries = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
            foreach (var mapping in plan.Mappings)
            {
                ct.ThrowIfCancellationRequested();
                var target = SafeZip.ResolveInside(fs, staging, mapping.ProfileRelativeTarget);
                await SafeZip.ExtractEntryAsync(fs, entries[mapping.ZipEntry], target, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            DeleteDirectory(staging);
            throw;
        }

        if (plan.Mappings.Count == 0)
        {
            DeleteDirectory(staging);
            throw new SilkflyException("Error_EmptyPackage", id.FullName);
        }

        var ownedByExisting = new HashSet<string>(existing?.Files ?? [], StringComparer.OrdinalIgnoreCase);
        var owners = OwnerMap(lockFile, except: id);
        var journal = new OperationJournal { OperationId = opId, Description = $"install {id} {version}" };
        var files = new List<string>();

        foreach (var file in existing?.Files ?? [])
        {
            if (IsConfig(file))
                continue;
            var physical = Physical(file, existing!.Enabled);
            if (fs.File.Exists(Abs(profile, physical)))
                journal.Moves.Add(new JournalMove(physical, $"{StagingRelative(opId)}.backup/{file}"));
        }

        foreach (var mapping in plan.Mappings)
        {
            var logical = mapping.ProfileRelativeTarget;
            var physical = Physical(logical, enabled);
            files.Add(logical);

            if (fs.File.Exists(Abs(profile, physical)))
            {
                if (mapping.PreserveIfExists)
                    continue;
                if (!ownedByExisting.Contains(logical))
                {
                    DeleteDirectory(staging);
                    throw new InstallConflictException(owners.TryGetValue(logical, out var owner) ? $"{logical} ({owner})" : logical);
                }
            }

            journal.Moves.Add(new JournalMove($"{StagingRelative(opId)}/{logical}", physical));
        }

        var mod = new InstalledMod(id, version, enabled, isExplicit || existing?.Explicit == true, source, time.GetUtcNow(), files);
        await ApplyAsync(profile, journal, lockFile.WithMod(mod)).ConfigureAwait(false);
        logger.LogInformation("Installed {Package} {Version} into profile {Profile} ({Count} files)", id, version, profile.Name, files.Count);
        return mod;
    }

    public async Task UninstallAsync(Profile profile, PackageId id, bool keepConfigs, CancellationToken ct)
    {
        var lockFile = profiles.GetLock(profile);
        var mod = lockFile.Find(id) ?? throw new SilkflyException("Error_ModNotInstalled", id.FullName);
        var opId = Guid.NewGuid();
        var journal = new OperationJournal { OperationId = opId, Description = $"uninstall {id}" };

        foreach (var file in mod.Files)
        {
            if (IsConfig(file) && keepConfigs)
                continue;
            var physical = Physical(file, mod.Enabled);
            if (fs.File.Exists(Abs(profile, physical)))
                journal.Moves.Add(new JournalMove(physical, $"{StagingRelative(opId)}.trash/{file}"));
        }

        await ApplyAsync(profile, journal, lockFile.WithoutMod(id)).ConfigureAwait(false);
        logger.LogInformation("Uninstalled {Package} from profile {Profile}", id, profile.Name);
    }

    public async Task SetEnabledAsync(Profile profile, PackageId id, bool enabled, CancellationToken ct)
    {
        var lockFile = profiles.GetLock(profile);
        var mod = lockFile.Find(id) ?? throw new SilkflyException("Error_ModNotInstalled", id.FullName);
        if (mod.Enabled == enabled)
            return;

        var journal = new OperationJournal { OperationId = Guid.NewGuid(), Description = $"{(enabled ? "enable" : "disable")} {id}" };
        foreach (var file in mod.Files.Where(f => !IsConfig(f)))
        {
            var from = Physical(file, mod.Enabled);
            var to = Physical(file, enabled);
            if (!fs.File.Exists(Abs(profile, from)))
                continue;
            if (fs.File.Exists(Abs(profile, to)))
                throw new InstallConflictException(to);
            journal.Moves.Add(new JournalMove(from, to));
        }

        await ApplyAsync(profile, journal, lockFile.WithMod(mod with { Enabled = enabled })).ConfigureAwait(false);
    }

    public ManualPackageInfo IdentifyManual(string filePath, Func<string, SemVersion, PackageId?> findInCatalog)
    {
        var fileName = fs.Path.GetFileNameWithoutExtension(filePath);
        if (filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return new ManualPackageInfo(new PackageId("manual", Sanitize(fileName)), Versions.Zero, ModSource.ManualDll);

        using var zip = SafeZip.OpenRead(fs, filePath);
        var manifest = zip.Entries.FirstOrDefault(e =>
            SafeZip.Normalize(e.FullName).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) ?? zip.Entries.FirstOrDefault(e =>
            SafeZip.Normalize(e.FullName).Count(c => c == '/') == 1 && e.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase));

        if (manifest is not null && ReadManifest(manifest) is { } m)
        {
            var version = Versions.TryParse(m.Version, out var v) ? v : Versions.Zero;

            var parts = fileName.Split('-');
            if (parts.Length >= 2 && parts[1].Equals(m.Name, StringComparison.OrdinalIgnoreCase))
                return new ManualPackageInfo(new PackageId(parts[0], m.Name), version, ModSource.ManualZip);

            if (findInCatalog(m.Name, version) is { } known)
                return new ManualPackageInfo(known, version, ModSource.ManualZip);

            return new ManualPackageInfo(new PackageId("manual", Sanitize(m.Name)), version, ModSource.ManualZip);
        }

        return new ManualPackageInfo(new PackageId("manual", Sanitize(fileName)), Versions.Zero, ModSource.ManualZip);
    }

    public async Task<InstalledMod> InstallManualAsync(Profile profile, string filePath, ManualPackageInfo info, CancellationToken ct)
    {
        if (info.Source != ModSource.ManualDll)
            return await InstallArchiveAsync(profile, filePath, info.Id, info.Version, info.Source, isExplicit: true, ct).ConfigureAwait(false);

        var temp = fs.Path.Combine(profile.StagingPath, $"{Guid.NewGuid()}.zip");
        fs.Directory.CreateDirectory(profile.StagingPath);
        try
        {
            await using (var stream = fs.File.Create(temp))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(fs.Path.GetFileName(filePath));
                await using var output = entry.Open();
                await using var input = fs.File.OpenRead(filePath);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }

            return await InstallArchiveAsync(profile, temp, info.Id, info.Version, info.Source, isExplicit: true, ct).ConfigureAwait(false);
        }
        finally
        {
            if (fs.File.Exists(temp))
                fs.File.Delete(temp);
        }
    }

    public IReadOnlyList<UnmanagedItem> ScanUnmanaged(Profile profile)
    {
        var lockFile = profiles.GetLock(profile);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in lockFile.Mods.SelectMany(m => m.Files))
        {
            if (file.StartsWith(PluginsFolder + "/", StringComparison.OrdinalIgnoreCase))
                owned.Add(file[(PluginsFolder.Length + 1)..].Split('/')[0]);
        }

        var items = new List<UnmanagedItem>();
        AddEntries(fs.Path.Combine(profile.RootPath, "BepInEx", "plugins"), PluginsFolder, disabled: false);
        AddEntries(fs.Path.Combine(profile.RootPath, "disabled", "unmanaged"), UnmanagedDisabledFolder, disabled: true);
        return items;

        void AddEntries(string folder, string relativeFolder, bool disabled)
        {
            if (!fs.Directory.Exists(folder))
                return;
            foreach (var dir in fs.Directory.GetDirectories(folder))
            {
                var name = fs.Path.GetFileName(dir);
                if (disabled || !owned.Contains(name))
                    items.Add(new UnmanagedItem($"{relativeFolder}/{name}", true, disabled));
            }

            foreach (var file in fs.Directory.GetFiles(folder))
            {
                var name = fs.Path.GetFileName(file);
                if (disabled || !owned.Contains(name))
                    items.Add(new UnmanagedItem($"{relativeFolder}/{name}", false, disabled));
            }
        }
    }

    public Task RemoveUnmanagedAsync(Profile profile, UnmanagedItem item, CancellationToken ct)
    {
        var path = Abs(profile, item.RelativePath);
        if (item.IsDirectory)
            DeleteDirectory(path);
        else if (fs.File.Exists(path))
            fs.File.Delete(path);
        logger.LogInformation("Removed unmanaged {Item} from profile {Profile}", item.RelativePath, profile.Name);
        return Task.CompletedTask;
    }

    public Task SetUnmanagedEnabledAsync(Profile profile, UnmanagedItem item, bool enabled, CancellationToken ct)
    {
        if (item.LooksDisabled != enabled)
            return Task.CompletedTask;

        var target = enabled ? $"{PluginsFolder}/{item.Name}" : $"{UnmanagedDisabledFolder}/{item.Name}";
        var from = Abs(profile, item.RelativePath);
        var to = Abs(profile, target);
        if (fs.File.Exists(to) || fs.Directory.Exists(to))
            throw new InstallConflictException(target);

        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(to)!);
        if (item.IsDirectory)
            fs.Directory.Move(from, to);
        else
            fs.File.Move(from, to);
        return Task.CompletedTask;
    }

    public bool IsIntact(Profile profile, InstalledMod mod) =>
        mod.Files.All(f => IsConfig(f) || fs.File.Exists(Abs(profile, Physical(f, mod.Enabled))));

    public async Task RecoverAsync(Profile profile, CancellationToken ct)
    {
        var staging = profile.StagingPath;
        if (!fs.Directory.Exists(staging))
            return;

        foreach (var journalFile in fs.Directory.GetFiles(staging, "*.journal.json"))
        {
            var journal = OperationJournal.Load(fs, journalFile);
            if (journal is null)
                continue;

            if (journal.Committed)
            {
                logger.LogInformation("Cleaning up finished operation {Op} ({What})", journal.OperationId, journal.Description);
            }
            else
            {
                logger.LogWarning("Rolling back interrupted operation {Op} ({What}) in profile {Profile}",
                    journal.OperationId, journal.Description, profile.Name);
                Rollback(profile, journal);
            }
        }

        DeleteDirectory(staging);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ApplyAsync(Profile profile, OperationJournal journal, ProfileLock newLock)
    {
        fs.Directory.CreateDirectory(profile.StagingPath);
        await journal.SaveAsync(fs, profile.StagingPath, CancellationToken.None).ConfigureAwait(false);

        try
        {
            foreach (var move in journal.Moves)
                Move(profile, move.From, move.To);

            await profiles.SaveLockAsync(profile, newLock, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Operation {Op} ({What}) failed, rolling back", journal.OperationId, journal.Description);
            Rollback(profile, journal);
            Cleanup(profile, journal.OperationId);
            throw ex is UnauthorizedAccessException ? new InsufficientPermissionsException(profile.RootPath, ex) : ex;
        }

        journal.Committed = true;
        await journal.SaveAsync(fs, profile.StagingPath, CancellationToken.None).ConfigureAwait(false);
        Cleanup(profile, journal.OperationId);

        foreach (var move in journal.Moves)
            RemoveEmptyParents(profile, move.From);
    }

    private void Rollback(Profile profile, OperationJournal journal)
    {
        for (var i = journal.Moves.Count - 1; i >= 0; i--)
        {
            var move = journal.Moves[i];
            var to = Abs(profile, move.To);
            var from = Abs(profile, move.From);
            try
            {
                if (fs.File.Exists(to) && !fs.File.Exists(from))
                {
                    fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(from)!);
                    fs.File.Move(to, from);
                }
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Could not undo move {From} → {To}", move.From, move.To);
            }
        }
    }

    private void Cleanup(Profile profile, Guid opId)
    {
        DeleteDirectory(StagingFor(profile, opId));
        DeleteDirectory(StagingFor(profile, opId) + ".backup");
        DeleteDirectory(StagingFor(profile, opId) + ".trash");
        var journal = OperationJournal.PathFor(fs, profile.StagingPath, opId);
        if (fs.File.Exists(journal))
            fs.File.Delete(journal);
        if (fs.Directory.Exists(profile.StagingPath) && !fs.Directory.EnumerateFileSystemEntries(profile.StagingPath).Any())
            fs.Directory.Delete(profile.StagingPath);
    }

    private void Move(Profile profile, string from, string to)
    {
        var target = Abs(profile, to);
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(target)!);
        fs.File.Move(Abs(profile, from), target);
    }

    private void RemoveEmptyParents(Profile profile, string relativeFile)
    {
        // GetFullPath also expands 8.3 short names
        var dir = fs.Path.GetDirectoryName(fs.Path.GetFullPath(Abs(profile, relativeFile)));
        var root = fs.Path.GetFullPath(profile.RootPath).TrimEnd('\\', '/');
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            root,
            fs.Path.Combine(root, "BepInEx"),
            fs.Path.Combine(root, "BepInEx", "plugins"),
            fs.Path.Combine(root, "BepInEx", "patchers"),
            fs.Path.Combine(root, "BepInEx", "monomod"),
            fs.Path.Combine(root, "BepInEx", "config"),
            fs.Path.Combine(root, "BepInEx", "core"),
            fs.Path.Combine(root, ".staging"),
        };

        while (dir is not null && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !keep.Contains(dir.TrimEnd('\\', '/')))
        {
            if (!fs.Directory.Exists(dir) || fs.Directory.EnumerateFileSystemEntries(dir).Any())
                break;
            fs.Directory.Delete(dir);
            dir = fs.Path.GetDirectoryName(dir);
        }
    }

    private static Dictionary<string, string> OwnerMap(ProfileLock lockFile, PackageId except)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in lockFile.Mods.Where(m => m.Id != except))
        {
            foreach (var file in mod.Files)
                map[file] = mod.Id.FullName;
        }

        return map;
    }

    private static bool IsConfig(string logical) => logical.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase);

    private static string Physical(string logical, bool enabled) =>
        enabled || IsConfig(logical) ? logical : DisabledPrefix + logical;

    private static string StagingRelative(Guid opId) => $".staging/{opId}";

    private string StagingFor(Profile profile, Guid opId) => fs.Path.Combine(profile.StagingPath, opId.ToString());

    private string Abs(Profile profile, string relative) =>
        fs.Path.Combine(profile.RootPath, relative.Replace('/', fs.Path.DirectorySeparatorChar));

    private void DeleteDirectory(string path)
    {
        if (fs.Directory.Exists(path))
            fs.Directory.Delete(path, recursive: true);
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_').ToArray()).Trim('_');
        return cleaned.Length > 0 ? cleaned : "mod";
    }

    private static (string Name, string? Version)? ReadManifest(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var version = root.TryGetProperty("version_number", out var v) ? v.GetString() : null;
            return string.IsNullOrWhiteSpace(name) ? null : (name, version);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
