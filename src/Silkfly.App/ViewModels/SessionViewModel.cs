using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Silkfly.App.Localization;
using Silkfly.App.Services;
using Silkfly.Core.Catalog;
using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Launch;
using Silkfly.Core.Loader;
using Silkfly.Core.Models;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;

namespace Silkfly.App.ViewModels;

public sealed partial class ProfileItemViewModel(
    Profile profile, int modCount, ILocalizer localizer, TimeProvider time, ProfileSharing sharing, ProfileIcons icons) : ObservableObject
{
    private int _modCount = modCount;
    private bool _iconRequested;
    private Avalonia.Media.Imaging.Bitmap? _icon;

    public Avalonia.Media.Imaging.Bitmap? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }

            return _icon;
        }
    }

    public bool HasIcon => Icon is not null;

    public bool HasIconFile => Profile.IconFile is not null;

    private async Task LoadIconAsync()
    {
        _icon = await icons.LoadAsync(Profile);
        if (_icon is not null)
        {
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(HasIcon));
        }
    }

    [RelayCommand]
    private Task ChooseIconAsync() => icons.ChooseAsync(Profile);

    [RelayCommand]
    private Task RemoveIconAsync() => icons.RemoveAsync(Profile);

    public Profile Profile { get; } = profile;

    public string Name => Profile.Name;

    public string ModsText => localizer.Format("Profiles_Mods", _modCount);

    public void SetModCount(int count)
    {
        _modCount = count;
        OnPropertyChanged(nameof(ModsText));
    }

    public string LastPlayedText => Profile.LastPlayedAt is { } at
        ? localizer.Format("Profiles_LastPlayed", RelativeTime.Format(localizer, time.GetUtcNow() - at))
        : localizer["Profiles_NeverPlayed"];

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private Task ExportFileAsync() => sharing.ExportToFileAsync(Profile);

    [RelayCommand]
    private Task ShareCodeAsync() => sharing.ShareCodeAsync(Profile);

    public override string ToString() => Name;
}

public sealed partial class SessionViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGameLocator _locator;
    private readonly ILoaderService _loader;
    private readonly IProfileService _profiles;
    private readonly ILaunchService _launcher;
    private readonly IModInstaller _installer;
    private readonly Lazy<ProfileSharing> _sharing;
    private readonly Lazy<ProfileIcons> _icons;
    private readonly ICatalogService _catalog;
    private readonly ILocalizer _localizer;
    private readonly IToastService _toasts;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionViewModel> _logger;
    private bool _syncingActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame), nameof(GamePathText), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(LaunchModdedCommand), nameof(LaunchVanillaCommand), nameof(InstallLoaderCommand), nameof(RemoveLoaderCommand))]
    private GameInstall? _game;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoaderText), nameof(IsLoaderInstalled), nameof(LoaderButtonText), nameof(StatusText), nameof(ForeignLoader))]
    [NotifyCanExecuteChangedFor(nameof(LaunchModdedCommand), nameof(RemoveLoaderCommand))]
    private LoaderStatus? _loaderStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallLoaderCommand), nameof(RemoveLoaderCommand))]
    private bool _isInstallingLoader;

    [ObservableProperty]
    private double _loaderProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(LaunchModdedCommand))]
    private ProfileItemViewModel? _activeProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayTooltip))]
    [NotifyCanExecuteChangedFor(nameof(LaunchModdedCommand), nameof(LaunchVanillaCommand))]
    private bool _isGameRunning;

    public SessionViewModel(
        ISettingsService settings,
        IGameLocator locator,
        ILoaderService loader,
        IProfileService profiles,
        ILaunchService launcher,
        IModInstaller installer,
        Lazy<ProfileSharing> sharing,
        Lazy<ProfileIcons> icons,
        ICatalogService catalog,
        ILocalizer localizer,
        IToastService toasts,
        TimeProvider time,
        ILogger<SessionViewModel> logger)
    {
        _settings = settings;
        _locator = locator;
        _loader = loader;
        _profiles = profiles;
        _launcher = launcher;
        _installer = installer;
        _sharing = sharing;
        _icons = icons;
        _catalog = catalog;
        _localizer = localizer;
        _toasts = toasts;
        _time = time;
        _logger = logger;

        profiles.ProfilesChanged += (_, _) => Dispatcher.UIThread.Post(RebuildProfiles);
        profiles.ActiveChanged += (_, _) => Dispatcher.UIThread.Post(SyncActive);
        launcher.RunningChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            IsGameRunning = launcher.IsGameRunning;
            RebuildProfiles();
        });
        catalog.CatalogChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshLoaderStatusAsync());
        localizer.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(string.Empty);
            RebuildProfiles();
        };
    }

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    public bool HasGame => Game is not null;

    public bool IsLoaderInstalled => LoaderStatus?.IsInstalled == true;

    public bool ForeignLoader => LoaderStatus?.ForeignLoaderDetected == true;

    public string GamePathText => Game?.Path ?? _localizer["Settings_Game_NotFound"];

    public string LoaderText => LoaderStatus switch
    {
        null or { IsInstalled: false } => _localizer["Loader_NotInstalled"],
        { UpdateAvailable: true } s => _localizer.Format("Loader_UpdateAvailable", s.InstalledPackVersion!, s.LatestPackVersion!),
        var s => _localizer.Format("Loader_Installed", s.InstalledPackVersion?.ToString() ?? "?"),
    };

    public string LoaderButtonText => IsLoaderInstalled ? _localizer["Loader_Update"] : _localizer["Loader_Install"];

    public string StatusText => !HasGame
        ? _localizer["Info_Status_NoGame"]
        : $"{(ActiveProfile is { } p ? _localizer.Format("Info_Status_Profile", p.Name) : _localizer["Info_Status_ProfileNone"])} · {_localizer.Format("Info_Status_Loader", LoaderText)}";

    public string PlayTooltip => IsGameRunning ? _localizer["Header_Running"] : _localizer["Header_Play"];

    public bool NeedsOnboarding => Game is null || !IsLoaderInstalled || Profiles.Count == 0;

    public IGameLocator Locator => _locator;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _profiles.LoadAsync(ct);

        // roll back interrupted operations first
        foreach (var profile in _profiles.Profiles)
            await _installer.RecoverAsync(profile, ct);
        RebuildProfiles();
        var game = _settings.Current.GamePath is { } saved ? _locator.Validate(saved).Install : null;
        if (game is null)
        {
            game = await Task.Run(() => _locator.DetectInstalls().FirstOrDefault(), ct);
            if (game is not null)
                await _settings.UpdateAsync(s => s with { GamePath = game.Path }, ct);
        }

        Game = game;
        await RefreshLoaderStatusAsync();
    }

    public async Task SetGameAsync(GameInstall game)
    {
        Game = game;
        await _settings.UpdateAsync(s => s with { GamePath = game.Path });
        await RefreshLoaderStatusAsync();
    }

    public async Task RefreshLoaderStatusAsync()
    {
        LoaderStatus = Game is null ? null : await _loader.GetStatusAsync(Game, CancellationToken.None);
        LaunchModdedCommand.NotifyCanExecuteChanged();
    }

    public Task<Profile> CreateProfileAsync(string name) => _profiles.CreateAsync(name, CancellationToken.None);

    public string? ValidateProfileName(string name, Profile? except = null)
    {
        try
        {
            _profiles.ValidateName(name.Trim(), except);
            return null;
        }
        catch (SilkflyException ex)
        {
            return _localizer.Format(ex.MessageKey, [.. ex.Args]);
        }
    }

    partial void OnActiveProfileChanged(ProfileItemViewModel? value)
    {
        if (_syncingActive || value is null || value.Profile.Id == _profiles.Active?.Id)
            return;
        _ = _profiles.SetActiveAsync(value.Profile, CancellationToken.None);
    }

    private bool CanInstallLoader() => Game is not null && !IsInstallingLoader;

    [RelayCommand(CanExecute = nameof(CanInstallLoader))]
    private async Task InstallLoaderAsync()
    {
        if (Game is null)
            return;

        IsInstallingLoader = true;
        LoaderProgress = 0;
        try
        {
            if (_catalog.Find(CatalogRules.LoaderPackage) is null)
                await _catalog.RefreshAsync(force: false, CancellationToken.None);

            await Task.Run(() => _loader.InstallOrUpdateAsync(
                Game, new Progress<double>(p => Dispatcher.UIThread.Post(() => LoaderProgress = p)), CancellationToken.None));
            _toasts.Show(_localizer["Loader_Done"], ToastKind.Success);
        }
        catch (Exception ex) when (ex is SilkflyException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Loader installation failed");
            _toasts.ShowError("Loader_Install", ex);
        }
        finally
        {
            IsInstallingLoader = false;
            await RefreshLoaderStatusAsync();
        }
    }

    private bool CanRemoveLoader() => Game is not null && IsLoaderInstalled && !IsInstallingLoader;

    [RelayCommand(CanExecute = nameof(CanRemoveLoader))]
    private async Task RemoveLoaderAsync()
    {
        try
        {
            await _loader.RemoveAsync(Game!, CancellationToken.None);
            _toasts.Show(_localizer["Loader_Removed"]);
        }
        catch (Exception ex) when (ex is SilkflyException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Loader removal failed");
            _toasts.ShowError("Loader_Remove", ex);
        }

        await RefreshLoaderStatusAsync();
    }

    private bool CanLaunchModded() => Game is not null && IsLoaderInstalled && ActiveProfile is not null && !IsGameRunning;

    [RelayCommand(CanExecute = nameof(CanLaunchModded))]
    private Task LaunchModdedAsync() => LaunchAsync(ActiveProfile?.Profile);

    private bool CanLaunchVanilla() => Game is not null && !IsGameRunning;

    [RelayCommand(CanExecute = nameof(CanLaunchVanilla))]
    private Task LaunchVanillaAsync() => LaunchAsync(null);

    public async Task LaunchAsync(Profile? profile)
    {
        var result = profile is null
            ? await _launcher.LaunchVanillaAsync(CancellationToken.None)
            : await _launcher.LaunchModdedAsync(profile, CancellationToken.None);

        if (result.Started)
            _toasts.Show(_localizer["Launch_Started"]);
        else if (result.ErrorKey is not null)
            _toasts.Show(_localizer.Format(result.ErrorKey, result.ErrorArgs ?? []), ToastKind.Error);
    }

    public void RefreshProfileCounts()
    {
        foreach (var item in Profiles)
            item.SetModCount(_profiles.GetLock(item.Profile).Mods.Count);
    }

    private void RebuildProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _profiles.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var mods = _profiles.GetLock(profile).Mods.Count;
            Profiles.Add(new ProfileItemViewModel(profile, mods, _localizer, _time, _sharing.Value, _icons.Value));
        }

        SyncActive();
        OnPropertyChanged(nameof(NeedsOnboarding));
    }

    private void SyncActive()
    {
        _syncingActive = true;
        foreach (var item in Profiles)
            item.IsActive = item.Profile.Id == _profiles.Active?.Id;
        ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
        _syncingActive = false;
    }
}
