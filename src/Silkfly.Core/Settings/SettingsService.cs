using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;

namespace Silkfly.Core.Settings;

public sealed class SettingsService(
    IFileSystem fs,
    IAppPaths paths,
    TimeProvider time,
    ILogger<SettingsService> logger) : ISettingsService
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public string? CorruptFileBackupPath { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public void Load()
    {
        var file = paths.SettingsFile;
        if (!fs.File.Exists(file))
        {
            logger.LogInformation("No settings file at {Path}, using defaults", file);
            Current = new AppSettings();
            return;
        }

        try
        {
            var json = fs.File.ReadAllText(file);
            Current = (JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                       ?? throw new JsonException("settings.json contains null")).Normalized();
        }
        catch (JsonException ex)
        {
            var backup = $"{file}.bad-{time.GetUtcNow():yyyyMMddHHmmss}";
            fs.File.Move(file, backup, overwrite: true);
            CorruptFileBackupPath = backup;
            Current = new AppSettings();
            logger.LogWarning(ex, "settings.json is corrupt, moved to {Backup}; using defaults", backup);
        }
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default)
    {
        AppSettings updated;
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            updated = mutate(Current);
            if (updated == Current)
                return;

            var json = JsonSerializer.Serialize(updated, SettingsJsonContext.Default.AppSettings);
            await AtomicFile.WriteAllTextAsync(fs, paths.SettingsFile, json, ct: ct).ConfigureAwait(false);
            Current = updated;
        }
        finally
        {
            _writeLock.Release();
        }

        Changed?.Invoke(this, updated);
    }
}
