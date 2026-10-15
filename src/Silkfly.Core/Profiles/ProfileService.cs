using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Loader;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Profiles;

public interface IProfileService
{
    IReadOnlyList<Profile> Profiles { get; }

    Profile? Active { get; }

    event EventHandler? ProfilesChanged;

    event EventHandler<Profile?>? ActiveChanged;

    Task LoadAsync(CancellationToken ct);

    Task<Profile> CreateAsync(string name, CancellationToken ct);

    Task<Profile> DuplicateAsync(Profile source, string newName, CancellationToken ct);

    Task<Profile> RenameAsync(Profile profile, string newName, CancellationToken ct);

    Task DeleteAsync(Profile profile, CancellationToken ct);

    Task SetActiveAsync(Profile profile, CancellationToken ct);

    Task MarkPlayedAsync(Profile profile, CancellationToken ct);

    Task<Profile> SetIconAsync(Profile profile, string? sourceFile, CancellationToken ct);

    Task MoveRootAsync(string newRoot, IProgress<double>? progress, CancellationToken ct);

    ProfileLock GetLock(Profile profile);

    Task SaveLockAsync(Profile profile, ProfileLock lockFile, CancellationToken ct);

    void ValidateName(string name, Profile? except = null);
}

public sealed class ProfileService(
    IFileSystem fs,
    IAppPaths paths,
    IProfileLockStore locks,
    ILoaderService loader,
    ISettingsService settings,
    TimeProvider time,
    ILogger<ProfileService> logger) : IProfileService
{
    public const int MaxNameLength = 64;

    public const long MaxIconBytes = 2 * 1024 * 1024;

    public static readonly IReadOnlyList<string> IconExtensions = [".png", ".jpg", ".jpeg"];

    private const string IconFileStem = "icon";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<Profile> _profiles = [];

    public IReadOnlyList<Profile> Profiles => _profiles;

    public Profile? Active { get; private set; }

    public event EventHandler? ProfilesChanged;

    public event EventHandler<Profile?>? ActiveChanged;

    public async Task LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var found = new List<Profile>();
            if (fs.Directory.Exists(paths.ProfilesRoot))
            {
                foreach (var dir in fs.Directory.GetDirectories(paths.ProfilesRoot))
                {
                    var lockFile = locks.Load(dir);
                    if (lockFile is not null)
                        found.Add(ToProfile(dir, lockFile));
                }
            }

            _profiles = found.OrderBy(p => p.CreatedAt).ToList();
            logger.LogInformation("Loaded {Count} profile(s) from {Root}", _profiles.Count, paths.ProfilesRoot);
        }
        finally
        {
            _gate.Release();
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        var active = _profiles.FirstOrDefault(p => p.Id == settings.Current.ActiveProfileId) ?? _profiles.FirstOrDefault();
        await SetActiveInternalAsync(active, ct).ConfigureAwait(false);
    }

    public async Task<Profile> CreateAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        ValidateName(name);

        var id = Guid.NewGuid();
        var root = fs.Path.Combine(paths.ProfilesRoot, id.ToString());
        foreach (var folder in new[] { "plugins", "patchers", "config", "core" })
            fs.Directory.CreateDirectory(fs.Path.Combine(root, "BepInEx", folder));

        await loader.EnsureProfileCoreAsync(root, ct).ConfigureAwait(false);

        var lockFile = new ProfileLock { Id = id, Name = name, CreatedAt = time.GetUtcNow() };
        await locks.SaveAsync(root, lockFile, ct).ConfigureAwait(false);

        var profile = ToProfile(root, lockFile);
        _profiles = [.. _profiles, profile];
        logger.LogInformation("Created profile {Name} ({Id})", name, id);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);

        if (Active is null)
            await SetActiveInternalAsync(profile, ct).ConfigureAwait(false);
        return profile;
    }

    public async Task<Profile> DuplicateAsync(Profile source, string newName, CancellationToken ct)
    {
        newName = newName.Trim();
        ValidateName(newName);

        var id = Guid.NewGuid();
        var root = fs.Path.Combine(paths.ProfilesRoot, id.ToString());
        CopyDirectory(source.RootPath, root, ct);

        var lockFile = GetLock(source) with { Id = id, Name = newName, CreatedAt = time.GetUtcNow(), LastPlayedAt = null };
        await locks.SaveAsync(root, lockFile, ct).ConfigureAwait(false);
        var bak = fs.Path.Combine(root, Profile.LockFileName + ".bak");
        if (fs.File.Exists(bak))
            fs.File.Delete(bak);

        var profile = ToProfile(root, lockFile);
        _profiles = [.. _profiles, profile];
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    public async Task<Profile> RenameAsync(Profile profile, string newName, CancellationToken ct)
    {
        newName = newName.Trim();
        ValidateName(newName, profile);

        var lockFile = GetLock(profile) with { Name = newName };
        await locks.SaveAsync(profile.RootPath, lockFile, ct).ConfigureAwait(false);
        return Replace(profile, profile with { Name = newName });
    }

    public async Task DeleteAsync(Profile profile, CancellationToken ct)
    {
        await Task.Run(() => RecycleBin.DeleteDirectory(fs, profile.RootPath), ct).ConfigureAwait(false);
        _profiles = _profiles.Where(p => p.Id != profile.Id).ToList();
        logger.LogInformation("Deleted profile {Name} ({Id})", profile.Name, profile.Id);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);

        if (Active?.Id == profile.Id)
            await SetActiveInternalAsync(_profiles.FirstOrDefault(), ct).ConfigureAwait(false);
    }

    public Task SetActiveAsync(Profile profile, CancellationToken ct) => SetActiveInternalAsync(profile, ct);

    public async Task MarkPlayedAsync(Profile profile, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await locks.SaveAsync(profile.RootPath, GetLock(profile) with { LastPlayedAt = now }, ct).ConfigureAwait(false);
        Replace(profile, profile with { LastPlayedAt = now });
    }

    public async Task<Profile> SetIconAsync(Profile profile, string? sourceFile, CancellationToken ct)
    {
        string? iconName = null;
        if (sourceFile is not null)
        {
            var extension = fs.Path.GetExtension(sourceFile).ToLowerInvariant();
            if (!IconExtensions.Contains(extension))
                throw new SilkflyException("Profile_Error_IconFormat");
            if (fs.FileInfo.New(sourceFile).Length > MaxIconBytes)
                throw new SilkflyException("Profile_Error_IconTooLarge", MaxIconBytes / 1024 / 1024);
            iconName = IconFileStem + extension;
        }

        foreach (var old in IconExtensions.Select(e => fs.Path.Combine(profile.RootPath, IconFileStem + e)))
        {
            if (fs.File.Exists(old))
                fs.File.Delete(old);
        }

        if (sourceFile is not null)
            fs.File.Copy(sourceFile, fs.Path.Combine(profile.RootPath, iconName!), overwrite: true);

        await locks.SaveAsync(profile.RootPath, GetLock(profile) with { Icon = iconName }, ct).ConfigureAwait(false);
        return Replace(profile, profile with { IconFile = iconName });
    }

    public async Task MoveRootAsync(string newRoot, IProgress<double>? progress, CancellationToken ct)
    {
        var oldRoot = fs.Path.GetFullPath(paths.ProfilesRoot);
        newRoot = fs.Path.GetFullPath(newRoot);
        if (SamePath(oldRoot, newRoot))
            return;
        if (IsInside(newRoot, oldRoot) || IsInside(oldRoot, newRoot))
            throw new SilkflyException("Profile_Error_MoveNested");
        if (fs.Directory.Exists(newRoot) && fs.Directory.EnumerateFileSystemEntries(newRoot).Any())
            throw new SilkflyException("Profile_Error_MoveNotEmpty");

        var sources = fs.Directory.Exists(oldRoot) ? fs.Directory.GetDirectories(oldRoot) : [];
        var copied = new List<string>();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            fs.Directory.CreateDirectory(newRoot);
            for (var i = 0; i < sources.Length; i++)
            {
                var target = fs.Path.Combine(newRoot, fs.Path.GetFileName(sources[i]));
                copied.Add(target);
                await Task.Run(() => CopyDirectory(sources[i], target, ct), ct).ConfigureAwait(false);
                progress?.Report((i + 1.0) / sources.Length);
            }
        }
        catch
        {
            foreach (var target in copied.Where(fs.Directory.Exists))
                fs.Directory.Delete(target, recursive: true);
            throw;
        }
        finally
        {
            _gate.Release();
        }

        var isDefault = SamePath(newRoot, fs.Path.GetFullPath(paths.DefaultProfilesRoot));
        await settings.UpdateAsync(s => s with { ProfilesRoot = isDefault ? null : newRoot }, ct).ConfigureAwait(false);
        logger.LogInformation("Moved {Count} profile folder(s) from {Old} to {New}", sources.Length, oldRoot, newRoot);
        await LoadAsync(ct).ConfigureAwait(false);

        // delete old copies only after switching
        foreach (var source in sources)
        {
            try
            {
                fs.Directory.Delete(source, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete the old profile folder {Folder}", source);
            }
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('/', '\\') + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public ProfileLock GetLock(Profile profile) =>
        locks.Load(profile.RootPath) ?? new ProfileLock { Id = profile.Id, Name = profile.Name, CreatedAt = profile.CreatedAt };

    public Task SaveLockAsync(Profile profile, ProfileLock lockFile, CancellationToken ct) =>
        locks.SaveAsync(profile.RootPath, lockFile, ct);

    public void ValidateName(string name, Profile? except = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new SilkflyException("Profile_Error_NameEmpty");
        if (name.Length > MaxNameLength)
            throw new SilkflyException("Profile_Error_NameTooLong", MaxNameLength);
        if (name.Any(char.IsControl))
            throw new SilkflyException("Profile_Error_NameInvalid");
        if (_profiles.Any(p => p.Id != except?.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new SilkflyException("Profile_Error_NameTaken", name);
    }

    private async Task SetActiveInternalAsync(Profile? profile, CancellationToken ct)
    {
        Active = profile;
        if (settings.Current.ActiveProfileId != profile?.Id)
            await settings.UpdateAsync(s => s with { ActiveProfileId = profile?.Id }, ct).ConfigureAwait(false);
        ActiveChanged?.Invoke(this, profile);
    }

    private Profile Replace(Profile old, Profile updated)
    {
        _profiles = _profiles.Select(p => p.Id == old.Id ? updated : p).ToList();
        if (Active?.Id == old.Id)
        {
            Active = updated;
            ActiveChanged?.Invoke(this, updated);
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    private static Profile ToProfile(string root, ProfileLock l) =>
        new(l.Id, l.Name, root, l.CreatedAt, l.LastPlayedAt, l.Icon);

    private void CopyDirectory(string source, string target, CancellationToken ct)
    {
        fs.Directory.CreateDirectory(target);
        foreach (var file in fs.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = fs.Path.GetRelativePath(source, file);
            if (relative.StartsWith(".staging", StringComparison.OrdinalIgnoreCase))
                continue;
            var dest = fs.Path.Combine(target, relative);
            fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(dest)!);
            fs.File.Copy(file, dest);
        }
    }
}
