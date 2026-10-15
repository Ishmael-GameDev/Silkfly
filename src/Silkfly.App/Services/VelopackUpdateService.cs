using Microsoft.Extensions.Logging;
using Silkfly.Core.Settings;
using Silkfly.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace Silkfly.App.Services;

public sealed class VelopackUpdateService(ISettingsService settings, ILogger<VelopackUpdateService> logger) : IAppUpdateService
{
    public const string SourceOverrideVariable = "SILKFLY_UPDATE_SOURCE";

    private UpdateManager? _manager;
    private bool _managerIsBeta;
    private UpdateInfo? _found;

    public bool IsSupported => Manager().IsInstalled;

    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct)
    {
        var manager = Manager();
        if (!manager.IsInstalled)
            return null;

        _found = await manager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
        if (_found is null)
        {
            logger.LogInformation("Silkfly {Version} is up to date", manager.CurrentVersion);
            return null;
        }

        var target = _found.TargetFullRelease;
        logger.LogInformation("Silkfly {Version} is available (current {Current})", target.Version, manager.CurrentVersion);
        return new AppUpdateInfo(target.Version.ToString(), target.NotesMarkdown);
    }

    public async Task ApplyAndRestartAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var manager = Manager();
        var update = _found ?? await manager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
        if (update is null)
            return;

        await manager.DownloadUpdatesAsync(update, percent => progress?.Report(percent / 100.0), ct).ConfigureAwait(false);
        logger.LogInformation("Restarting into Silkfly {Version}", update.TargetFullRelease.Version);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }

    private UpdateManager Manager()
    {
        var beta = settings.Current.UpdateChannel == UpdateChannels.Beta;
        if (_manager is null || _managerIsBeta != beta)
        {
            _manager = Environment.GetEnvironmentVariable(SourceOverrideVariable) is { Length: > 0 } source
                ? new UpdateManager(source)
                : new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: beta));
            _managerIsBeta = beta;
            _found = null;
        }

        return _manager;
    }
}
