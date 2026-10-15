namespace Silkfly.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }

    string? CorruptFileBackupPath { get; }

    event EventHandler<AppSettings>? Changed;

    void Load();

    Task UpdateAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default);
}
