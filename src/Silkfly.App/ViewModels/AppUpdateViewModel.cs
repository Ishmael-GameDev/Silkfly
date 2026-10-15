using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Silkfly.App.Services;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Settings;
using Silkfly.Core.Updates;

namespace Silkfly.App.ViewModels;

public enum UpdateStatus { None, NotInstalled, Checking, UpToDate, Available, Failed }

public sealed partial class AppUpdateViewModel : ObservableObject
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly IAppUpdateService _updates;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IShellService _shell;
    private readonly IToastService _toasts;
    private readonly TimeProvider _time;
    private readonly ILogger<AppUpdateViewModel> _logger;

    public AppUpdateViewModel(
        IAppUpdateService updates,
        ISettingsService settings,
        ILocalizer localizer,
        IShellService shell,
        IToastService toasts,
        TimeProvider time,
        ILogger<AppUpdateViewModel> logger)
    {
        _updates = updates;
        _settings = settings;
        _localizer = localizer;
        _shell = shell;
        _toasts = toasts;
        _time = time;
        _logger = logger;
        localizer.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(BannerText));
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible), nameof(BannerText), nameof(StatusText))]
    private AppUpdateInfo? _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    private bool _isDismissed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsIdle))]
    private UpdateStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progress;

    public bool IsBannerVisible => Available is not null && !IsDismissed;

    public bool IsIdle => Status != UpdateStatus.Checking && !IsDownloading;

    public string BannerText => Available is { } update ? _localizer.Format("Update_Banner", update.Version) : string.Empty;

    public string StatusText => Status switch
    {
        UpdateStatus.NotInstalled => _localizer["Update_NotInstalled"],
        UpdateStatus.Checking => _localizer["Update_Checking"],
        UpdateStatus.UpToDate => _localizer["Update_UpToDate"],
        UpdateStatus.Available => _localizer.Format("Update_Banner", Available?.Version ?? string.Empty),
        UpdateStatus.Failed => _localizer["Update_Failed"],
        _ => string.Empty,
    };

    public async Task CheckOnStartupAsync()
    {
        await Task.Delay(StartupDelay).ConfigureAwait(true);
        if (_settings.Current.CheckForUpdates && UpdateSchedule.IsDue(_settings.Current.LastUpdateCheck, _time.GetUtcNow()))
            await CheckAsync(manual: false);
    }

    [RelayCommand]
    private Task CheckNowAsync() => CheckAsync(manual: true);

    private async Task CheckAsync(bool manual)
    {
        if (!IsIdle)
            return;

        if (!_updates.IsSupported)
        {
            Status = manual ? UpdateStatus.NotInstalled : UpdateStatus.None;
            return;
        }

        Status = UpdateStatus.Checking;
        try
        {
            Available = await _updates.CheckAsync(CancellationToken.None);
            IsDismissed = false;
            Status = Available is null ? UpdateStatus.UpToDate : UpdateStatus.Available;

            // only remember checks that found nothing
            if (Available is null)
                await _settings.UpdateAsync(s => s with { LastUpdateCheck = _time.GetUtcNow() });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            Status = manual ? UpdateStatus.Failed : UpdateStatus.None;
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (!IsIdle)
            return;

        IsDownloading = true;
        Progress = 0;
        try
        {
            await _updates.ApplyAndRestartAsync(new Progress<double>(p => Progress = p), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed");
            _toasts.Show(_localizer["Update_DownloadFailed"], ToastKind.Error, ex.Message);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Dismiss() => IsDismissed = true;

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        var url = Available is { } update
            ? $"{AppInfo.RepositoryUrl}/releases/tag/v{update.Version}"
            : $"{AppInfo.RepositoryUrl}/releases";
        _shell.OpenUrl(new Uri(url));
    }
}
