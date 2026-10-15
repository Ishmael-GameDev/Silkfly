using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Silkfly.App.Services;
using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.ViewModels;

public sealed class GameChoice(GameInstall install, ILocalizer localizer)
{
    public GameInstall Install { get; } = install;

    public string Path => Install.Path;

    public string SourceText => localizer[$"Game_Source_{Install.Source}"];
}

public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly SessionViewModel _session;
    private readonly IFilePicker _picker;
    private readonly ILocalizer _localizer;
    private readonly Action _close;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameStep), nameof(IsLoaderStep), nameof(IsProfileStep), nameof(StepText), nameof(NextText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    private int _step = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private GameChoice? _selectedGame;

    [ObservableProperty]
    private string? _gameError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _profileName;

    [ObservableProperty]
    private string? _profileError;

    public OnboardingViewModel(SessionViewModel session, IFilePicker picker, ILocalizer localizer, Action close)
    {
        _session = session;
        _picker = picker;
        _localizer = localizer;
        _close = close;
        _profileName = localizer["Onboarding_DefaultProfile"];

        foreach (var install in session.Locator.DetectInstalls())
            Games.Add(new GameChoice(install, localizer));
        if (session.Game is { } current && Games.All(g => !PathsEqual(g.Path, current.Path)))
            Games.Insert(0, new GameChoice(current, localizer));
        SelectedGame = Games.FirstOrDefault(g => session.Game is { } c && PathsEqual(g.Path, c.Path)) ?? Games.FirstOrDefault();

        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionViewModel.IsLoaderInstalled) or nameof(SessionViewModel.IsInstallingLoader))
                NextCommand.NotifyCanExecuteChanged();
        };
    }

    public SessionViewModel Session => _session;

    public ObservableCollection<GameChoice> Games { get; } = [];

    public bool HasNoGames => Games.Count == 0;

    public bool IsGameStep => Step == 1;

    public bool IsLoaderStep => Step == 2;

    public bool IsProfileStep => Step == 3;

    public string StepText => _localizer.Format("Onboarding_Step", Step);

    public string NextText => Step == 3 ? _localizer["Onboarding_Finish"] : _localizer["Onboarding_Next"];

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _picker.PickFolderAsync(_localizer["Onboarding_Game_PickTitle"]);
        if (folder is null)
            return;

        var result = _session.Locator.Validate(folder);
        if (result.Install is null)
        {
            GameError = string.Join(" ", result.Problems.Select(p => _localizer[p]));
            return;
        }

        GameError = null;
        var choice = new GameChoice(result.Install, _localizer);
        Games.Insert(0, choice);
        OnPropertyChanged(nameof(HasNoGames));
        SelectedGame = choice;
    }

    private bool CanGoBack() => Step > 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoNext() => Step switch
    {
        1 => SelectedGame is not null,
        2 => _session.IsLoaderInstalled && !_session.IsInstallingLoader,
        _ => !string.IsNullOrWhiteSpace(ProfileName) || _session.Profiles.Count > 0,
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        switch (Step)
        {
            case 1:
                await _session.SetGameAsync(SelectedGame!.Install);
                Step = 2;
                break;
            case 2:
                Step = 3;
                break;
            default:
                if (_session.Profiles.Count == 0 || !string.IsNullOrWhiteSpace(ProfileName))
                {
                    ProfileError = _session.ValidateProfileName(ProfileName);
                    if (ProfileError is not null && _session.Profiles.Count == 0)
                        return;
                    if (ProfileError is null)
                        await _session.CreateProfileAsync(ProfileName);
                }

                _close();
                break;
        }
    }

    [RelayCommand]
    private void Skip() => _close();

    private static bool PathsEqual(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
