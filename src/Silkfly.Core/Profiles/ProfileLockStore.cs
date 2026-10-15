using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;

namespace Silkfly.Core.Profiles;

public interface IProfileLockStore
{
    ProfileLock? Load(string profileRoot);

    Task SaveAsync(string profileRoot, ProfileLock lockFile, CancellationToken ct);

    event EventHandler<LockRepair>? LockRepaired;
}

public sealed record LockRepair(string ProfileRoot, bool FromBackup, string CorruptCopy);

public sealed class ProfileLockStore(IFileSystem fs, TimeProvider time, ILogger<ProfileLockStore> logger) : IProfileLockStore
{
    public event EventHandler<LockRepair>? LockRepaired;

    public ProfileLock? Load(string profileRoot)
    {
        var path = fs.Path.Combine(profileRoot, Profile.LockFileName);
        if (!fs.File.Exists(path))
            return null;

        if (TryRead(path, out var result))
            return result;

        var backup = path + ".bak";
        var corruptCopy = $"{path}.corrupt-{time.GetUtcNow():yyyyMMddHHmmss}";
        fs.File.Copy(path, corruptCopy, overwrite: true);

        if (fs.File.Exists(backup) && TryRead(backup, out var fromBackup))
        {
            logger.LogWarning("Lock of {Profile} was corrupt, restored from .bak (corrupt copy: {Copy})", profileRoot, corruptCopy);
            fs.File.Copy(backup, path, overwrite: true);
            LockRepaired?.Invoke(this, new LockRepair(profileRoot, FromBackup: true, corruptCopy));
            return fromBackup;
        }

        logger.LogWarning("Lock of {Profile} was corrupt and has no usable backup; starting empty (corrupt copy: {Copy})", profileRoot, corruptCopy);
        var name = fs.Path.GetFileName(profileRoot.TrimEnd('/', '\\'));
        var id = Guid.TryParse(name, out var parsed) ? parsed : Guid.NewGuid();
        var empty = new ProfileLock { Id = id, Name = name, CreatedAt = time.GetUtcNow() };
        fs.File.WriteAllText(path, Serialize(empty));
        LockRepaired?.Invoke(this, new LockRepair(profileRoot, FromBackup: false, corruptCopy));
        return empty;
    }

    public Task SaveAsync(string profileRoot, ProfileLock lockFile, CancellationToken ct) =>
        AtomicFile.WriteAllTextAsync(fs, fs.Path.Combine(profileRoot, Profile.LockFileName), Serialize(lockFile), keepBackup: true, ct);

    private bool TryRead(string path, out ProfileLock? result)
    {
        result = null;
        try
        {
            var dto = JsonSerializer.Deserialize(fs.File.ReadAllText(path), ProfileJsonContext.Default.LockDto);
            if (dto is null || dto.Id == Guid.Empty || string.IsNullOrWhiteSpace(dto.Name))
                return false;
            result = FromDto(dto);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            logger.LogWarning(ex, "Cannot read {Path}", path);
            return false;
        }
    }

    internal static string Serialize(ProfileLock lockFile) =>
        JsonSerializer.Serialize(ToDto(lockFile), ProfileJsonContext.Default.LockDto);

    private static LockDto ToDto(ProfileLock l) => new()
    {
        Schema = l.Schema,
        Id = l.Id,
        Name = l.Name,
        CreatedAt = l.CreatedAt,
        LastPlayedAt = l.LastPlayedAt,
        Icon = l.Icon,
        Loader = l.Loader is null ? null : new LoaderDto { Package = l.Loader.Package, Version = l.Loader.Version },
        Mods = l.Mods.Select(m => new ModDto
        {
            Id = m.Id.FullName,
            Version = m.Version.ToString(),
            Enabled = m.Enabled,
            Explicit = m.Explicit,
            Source = m.Source switch { ModSource.ManualZip => "manual-zip", ModSource.ManualDll => "manual-dll", _ => "thunderstore" },
            InstalledAt = m.InstalledAt,
            Files = m.Files.ToList(),
        }).ToList(),
    };

    private static ProfileLock FromDto(LockDto d) => new()
    {
        Schema = d.Schema,
        Id = d.Id,
        Name = d.Name!,
        CreatedAt = d.CreatedAt,
        LastPlayedAt = d.LastPlayedAt,
        Icon = d.Icon,
        Loader = d.Loader is { Package: { } p, Version: { } v } ? new LoaderRef(p, v) : null,
        Mods = (d.Mods ?? [])
            .Select(m => new InstalledMod(
                PackageId.ParseFullName(m.Id ?? throw new FormatException("mod without id")),
                Versions.Parse(m.Version ?? "0.0.0"),
                m.Enabled,
                m.Explicit,
                m.Source switch { "manual-zip" => ModSource.ManualZip, "manual-dll" => ModSource.ManualDll, _ => ModSource.Thunderstore },
                m.InstalledAt,
                m.Files ?? []))
            .ToList(),
    };
}

internal sealed class LockDto
{
    public int Schema { get; set; } = ProfileLock.CurrentSchema;
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public string? Icon { get; set; }
    public LoaderDto? Loader { get; set; }
    public List<ModDto>? Mods { get; set; }
}

internal sealed class LoaderDto
{
    public string? Package { get; set; }
    public string? Version { get; set; }
}

internal sealed class ModDto
{
    public string? Id { get; set; }
    public string? Version { get; set; }
    public bool Enabled { get; set; }
    public bool Explicit { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public List<string>? Files { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(LockDto))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
