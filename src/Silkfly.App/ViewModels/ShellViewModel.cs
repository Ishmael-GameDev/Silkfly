using CommunityToolkit.Mvvm.ComponentModel;
using Silkfly.App.Services;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.ViewModels;

public enum ShellPage { Info, Mods, Profiles, Settings }

public sealed partial class ShellViewModel(
    InfoViewModel info,
    ModListViewModel mods,
    ProfilesViewModel profiles,
    SettingsViewModel settings,
    SessionViewModel session,
    IToastService toasts,
    IFilePicker picker,
    ILocalizer localizer,
    AppUpdateViewModel updates) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentContent))]
    private ShellPage _currentPage = ShellPage.Mods;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboarding))]
    private OnboardingViewModel? _onboarding;

    public string Version { get; } = $"v{AppInfo.Version}";

    public ModListViewModel Mods => mods;

    public InfoViewModel Info => info;

    public AppUpdateViewModel Updates => updates;

    public SessionViewModel Session => session;

    public IToastService Toasts => toasts;

    public bool IsOnboarding => Onboarding is not null;

    public object CurrentContent => CurrentPage switch
    {
        ShellPage.Info => info,
        ShellPage.Mods => mods,
        ShellPage.Profiles => profiles,
        ShellPage.Settings => settings,
        _ => throw new ArgumentOutOfRangeException(nameof(CurrentPage), CurrentPage, null),
    };

    public void ShowOnboarding() =>
        Onboarding = new OnboardingViewModel(session, picker, localizer, () => Onboarding = null);
}
