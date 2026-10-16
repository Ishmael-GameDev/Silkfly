using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Silkfly.App.Services;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Updates;

namespace Silkfly.App.ViewModels;

public sealed partial class LanguageOption(string code, Func<string> displayName) : ObservableObject
{
    public string Code { get; } = code;

    public string DisplayName => displayName();

    public void Refresh() => OnPropertyChanged(nameof(DisplayName));
}

public sealed partial class LaunchModeOption(LaunchMode mode, string key, ILocalizer localizer) : ObservableObject
{
    public LaunchMode Mode { get; } = mode;

    public string DisplayName => localizer[key];

    public void Refresh() => OnPropertyChanged(nameof(DisplayName));
}

public sealed partial class PolicyOption(DependencyPolicy policy, string key, ILocalizer localizer) : ObservableObject
{
    public DependencyPolicy Policy { get; } = policy;

    public string DisplayName => localizer[key];

    public void Refresh() => OnPropertyChanged(nameof(DisplayName));
}

public sealed partial class ChannelOption(string channel, string key, ILocalizer localizer) : ObservableObject
{
    public string Channel { get; } = channel;

    public string DisplayName => localizer[key];

    public void Refresh() => OnPropertyChanged(nameof(DisplayName));
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IShellService _shell;
    private readonly IClipboardService _clipboard;
    private readonly IAppPaths _paths;
    private readonly IFilePicker _picker;
    private readonly IToastService _toasts;
    private readonly Lazy<ShellViewModel> _shellViewModel;
    private readonly Silkfly.Core.Install.IPackageCache _cache;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    public double ScrollOffset { get; set; }

    [ObservableProperty]
    private LaunchModeOption _selectedLaunchMode;

    [ObservableProperty]
    private bool _systemInfoCopied;

    [ObservableProperty]
    private string? _gameError;

    public SettingsViewModel(
        ISettingsService settings,
        ILocalizer localizer,
        IShellService shell,
        IClipboardService clipboard,
        IAppPaths paths,
        IFilePicker picker,
        IToastService toasts,
        SessionViewModel session,
        Lazy<ShellViewModel> shellViewModel,
        Silkfly.Core.Install.IPackageCache cache,
        AppUpdateViewModel updates,
        UiScaleLimits scaleLimits,
        IProfileService profiles,
        Silkfly.Core.Operations.IOperationQueue queue,
        IDialogService dialogs)
    {
        _profiles = profiles;
        _queue = queue;
        _dialogs = dialogs;
        _settings = settings;
        _localizer = localizer;
        _shell = shell;
        _clipboard = clipboard;
        _paths = paths;
        _picker = picker;
        _toasts = toasts;
        _shellViewModel = shellViewModel;
        _cache = cache;
        Session = session;
        Updates = updates;

        Languages =
        [
            new LanguageOption("auto", () => _localizer["Settings_Language_Auto"]),
            new LanguageOption("ru", () => "Русский"),
            new LanguageOption("en", () => "English"),
        ];
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == settings.Current.Language) ?? Languages[0];

        LaunchModes =
        [
            new LaunchModeOption(LaunchMode.Direct, "Settings_Launch_Direct", localizer),
            new LaunchModeOption(LaunchMode.ViaSteam, "Settings_Launch_ViaSteam", localizer),
            new LaunchModeOption(LaunchMode.ProtonConfig, "Settings_Launch_Proton", localizer),
        ];
        _selectedLaunchMode = LaunchModes.FirstOrDefault(m => m.Mode == settings.Current.LaunchMode) ?? LaunchModes[0];

        Policies =
        [
            new PolicyOption(DependencyPolicy.LatestSameMajor, "Settings_Policy_LatestSameMajor", localizer),
            new PolicyOption(DependencyPolicy.Exact, "Settings_Policy_Exact", localizer),
            new PolicyOption(DependencyPolicy.Latest, "Settings_Policy_Latest", localizer),
        ];
        _selectedPolicy = Policies.First(p => p.Policy == settings.Current.DependencyPolicy);
        _scaleLimits = scaleLimits;
        _selectedScale = NearestScale(settings.Current.UiScale);
        scaleLimits.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ScaleOptions));
            _selectedScale = NearestScale(_settings.Current.UiScale);
            OnPropertyChanged(nameof(SelectedScale));
        };
        _showDeprecated = settings.Current.ShowDeprecated;
        _showNsfw = settings.Current.ShowNsfw;
        _verboseLogging = settings.Current.VerboseLogging;
        _checkForUpdates = settings.Current.CheckForUpdates;
        Channels =
        [
            new ChannelOption(UpdateChannels.Stable, "Settings_Channel_Stable", localizer),
            new ChannelOption(UpdateChannels.Beta, "Settings_Channel_Beta", localizer),
        ];
        _selectedChannel = Channels.FirstOrDefault(c => c.Channel == settings.Current.UpdateChannel) ?? Channels[0];
        UpdateCacheText();

        settings.Changed += (_, s) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ShowDeprecated = s.ShowDeprecated;
            ShowNsfw = s.ShowNsfw;
        });

        _localizer.CultureChanged += (_, _) =>
        {
            foreach (var policy in Policies)
                policy.Refresh();
            foreach (var language in Languages)
                language.Refresh();
            foreach (var mode in LaunchModes)
                mode.Refresh();
            foreach (var channel in Channels)
                channel.Refresh();
            OnPropertyChanged(nameof(VersionText));
            OnPropertyChanged(nameof(LaunchHintText));
        };
    }

    public SessionViewModel Session { get; }

    public AppUpdateViewModel Updates { get; }

    public IReadOnlyList<ChannelOption> Channels { get; }

    [ObservableProperty]
    private ChannelOption _selectedChannel;

    [ObservableProperty]
    private bool _checkForUpdates;

    partial void OnCheckForUpdatesChanged(bool value) => _ = _settings.UpdateAsync(s => s with { CheckForUpdates = value });

    partial void OnSelectedChannelChanged(ChannelOption value) =>
        _ = _settings.UpdateAsync(s => s with { UpdateChannel = value.Channel, LastUpdateCheck = null });

    [RelayCommand]
    private void OpenRepository() => _shell.OpenUrl(new Uri(AppInfo.RepositoryUrl));

    [RelayCommand]
    private void OpenLicense() => OpenShippedFile("LICENSE");

    [RelayCommand]
    private void OpenThirdPartyNotices() => OpenShippedFile("THIRD-PARTY-NOTICES.md");

    private void OpenShippedFile(string name)
    {
        var local = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(local))
            _shell.OpenFile(local);
        else
            _shell.OpenUrl(new Uri($"{AppInfo.RepositoryUrl}/blob/main/{name}"));
    }

    public IReadOnlyList<PolicyOption> Policies { get; }

    private static readonly int[] AllScales = [90, 100, 110, 120, 130];

    private readonly UiScaleLimits _scaleLimits;

    public IReadOnlyList<int> ScaleOptions =>
        AllScales.Where(s => s == AllScales[0] || s <= _scaleLimits.ScreenMax * 100 + 0.5).ToList();

    private int NearestScale(double scale) =>
        ScaleOptions.OrderBy(s => Math.Abs(s - _scaleLimits.Clamp(scale) * 100)).First();

    [ObservableProperty]
    private PolicyOption _selectedPolicy;

    [ObservableProperty]
    private int _selectedScale;

    [ObservableProperty]
    private bool _showDeprecated;

    [ObservableProperty]
    private bool _showNsfw;

    [ObservableProperty]
    private bool _verboseLogging;

    partial void OnVerboseLoggingChanged(bool value) => _ = _settings.UpdateAsync(s => s with { VerboseLogging = value });

    [ObservableProperty]
    private string _cacheText = string.Empty;

    partial void OnSelectedPolicyChanged(PolicyOption value) => _ = _settings.UpdateAsync(s => s with { DependencyPolicy = value.Policy });

    partial void OnSelectedScaleChanged(int value) => _ = _settings.UpdateAsync(s => s with { UiScale = value / 100.0 });

    partial void OnShowDeprecatedChanged(bool value) => _ = _settings.UpdateAsync(s => s with { ShowDeprecated = value });

    partial void OnShowNsfwChanged(bool value) => _ = _settings.UpdateAsync(s => s with { ShowNsfw = value });

    private void UpdateCacheText() => CacheText = $"{_cache.SizeBytes / 1024.0 / 1024.0:0.#} MB";

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _cache.ClearAsync(CancellationToken.None);
        UpdateCacheText();
        _toasts.Show(_localizer["Settings_CacheCleared"], ToastKind.Success);
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public IReadOnlyList<LaunchModeOption> LaunchModes { get; }

    public string VersionText => _localizer.Format("Settings_Version", AppInfo.Version);

    public string DataFolder => _paths.DataRoot;

    private readonly IProfileService _profiles;
    private readonly Silkfly.Core.Operations.IOperationQueue _queue;
    private readonly IDialogService _dialogs;

    public string ProfilesFolder => _paths.ProfilesRoot;

    public bool IsCustomProfilesFolder => _settings.Current.ProfilesRoot is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveProfiles))]
    private bool _isMovingProfiles;

    [ObservableProperty]
    private double _moveProgress;

    public bool CanMoveProfiles => !IsMovingProfiles;

    [RelayCommand]
    private void OpenProfilesFolder() => _shell.OpenFolder(_paths.ProfilesRoot);

    [RelayCommand]
    private async Task ChangeProfilesFolderAsync()
    {
        var folder = await _picker.PickFolderAsync(_localizer["Settings_ProfilesFolder_Pick"]);
        if (folder is not null)
            await MoveProfilesAsync(folder);
    }

    [RelayCommand]
    private Task ResetProfilesFolderAsync() => MoveProfilesAsync(_paths.DefaultProfilesRoot);

    private async Task MoveProfilesAsync(string folder)
    {
        if (Session.IsGameRunning || _queue.Active.Count > 0)
        {
            _toasts.Show(_localizer["Settings_ProfilesFolder_Busy"], ToastKind.Warning);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer["Settings_ProfilesFolder_Title"],
            _localizer.Format("Settings_ProfilesFolder_Confirm", _profiles.Profiles.Count, folder),
            _localizer["Settings_ProfilesFolder_Move"]);
        if (!confirmed)
            return;

        IsMovingProfiles = true;
        MoveProgress = 0;
        try
        {
            await _profiles.MoveRootAsync(folder, new Progress<double>(p => MoveProgress = p), CancellationToken.None);
            _toasts.Show(_localizer["Settings_ProfilesFolder_Done"], ToastKind.Success);
        }
        catch (Exception ex) when (ex is SilkflyException or IOException or UnauthorizedAccessException)
        {
            _toasts.ShowError("Settings_ProfilesFolder_Failed", ex);
        }
        finally
        {
            IsMovingProfiles = false;
            OnPropertyChanged(nameof(ProfilesFolder));
            OnPropertyChanged(nameof(IsCustomProfilesFolder));
        }
    }

    async partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        _localizer.SetCulture(value.Code);
        await _settings.UpdateAsync(s => s with { Language = value.Code });
    }

    async partial void OnSelectedLaunchModeChanged(LaunchModeOption value)
    {
        await _settings.UpdateAsync(s => s with { LaunchMode = value.Mode });
        OnPropertyChanged(nameof(HasLaunchHint));
        OnPropertyChanged(nameof(LaunchHintText));
        OnPropertyChanged(nameof(LaunchOptionsText));
    }

    public string? LaunchOptionsText => SelectedLaunchMode.Mode switch
    {
        LaunchMode.ProtonConfig => "WINEDLLOVERRIDES=\"winhttp=n,b\" %command%",
        LaunchMode.ViaSteam when !OperatingSystem.IsWindows() => "./run_bepinex.sh %command%",
        _ => null,
    };

    public bool HasLaunchHint => SelectedLaunchMode.Mode != LaunchMode.Direct;

    public string LaunchHintText => SelectedLaunchMode.Mode switch
    {
        LaunchMode.ProtonConfig => _localizer["Launch_Hint_Proton"],
        LaunchMode.ViaSteam when OperatingSystem.IsWindows() => _localizer["Launch_Hint_SteamWindows"],
        _ => _localizer["Launch_Hint_Steam"],
    };

    [ObservableProperty]
    private bool _launchOptionsCopied;

    [RelayCommand]
    private async Task CopyLaunchOptionsAsync()
    {
        if (LaunchOptionsText is not { } text)
            return;
        await _clipboard.SetTextAsync(text);
        LaunchOptionsCopied = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        LaunchOptionsCopied = false;
    }

    [RelayCommand]
    private async Task ChangeGamePathAsync()
    {
        var folder = await _picker.PickFolderAsync(_localizer["Onboarding_Game_PickTitle"]);
        if (folder is null)
            return;

        var result = Session.Locator.Validate(folder);
        if (result.Install is null)
        {
            GameError = string.Join(" ", result.Problems.Select(p => _localizer[p]));
            return;
        }

        GameError = null;
        await Session.SetGameAsync(result.Install);
        _toasts.Show(_localizer["Settings_GameChanged"], ToastKind.Success);
    }

    [RelayCommand]
    private void RunOnboarding() => _shellViewModel.Value.ShowOnboarding();

    [RelayCommand]
    private void OpenLogs() => _shell.OpenFolder(_paths.LogsRoot);

    [RelayCommand]
    private void OpenDataFolder() => _shell.OpenFolder(_paths.DataRoot);

    [RelayCommand]
    private async Task CopySystemInfoAsync()
    {
        await _clipboard.SetTextAsync(AppInfo.SystemInfo(_paths.DataRoot, _localizer.Culture.Name));
        SystemInfoCopied = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        SystemInfoCopied = false;
    }
}
