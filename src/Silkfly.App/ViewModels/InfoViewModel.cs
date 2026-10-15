using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.News;

namespace Silkfly.App.ViewModels;

public sealed partial class InfoViewModel : ObservableObject
{
    private readonly IShellService _shell;
    private readonly IAppPaths _paths;
    private readonly INewsService _newsService;
    private readonly ILocalizer _localizer;
    private readonly ILogger<InfoViewModel> _logger;

    public InfoViewModel(
        SessionViewModel session,
        IShellService shell,
        IAppPaths paths,
        INewsService newsService,
        ILocalizer localizer,
        ILogger<InfoViewModel> logger)
    {
        Session = session;
        _shell = shell;
        _paths = paths;
        _newsService = newsService;
        _localizer = localizer;
        _logger = logger;
        localizer.CultureChanged += (_, _) => OnPropertyChanged(nameof(NewsText));
    }

    public SessionViewModel Session { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewsText), nameof(HasNews), nameof(HasNewsLink))]
    private NewsItem? _news;

    public bool HasNews => News is not null;

    public bool HasNewsLink => News?.Link is not null;

    public string NewsText => News?.TextFor(_localizer.Culture) ?? string.Empty;

    public async Task LoadNewsAsync()
    {
        try
        {
            News = await _newsService.GetLatestAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading news failed");
        }
    }

    [RelayCommand]
    private void OpenNews()
    {
        if (News?.Link is { } link)
            _shell.OpenUrl(link);
    }

    [RelayCommand]
    private void OpenThunderstore() => _shell.OpenUrl(new Uri("https://thunderstore.io/c/hollow-knight-silksong/"));

    [RelayCommand]
    private void OpenDiscord() => _shell.OpenUrl(new Uri("https://discord.com/invite/VDsg3HmWuB"));

    [RelayCommand]
    private void OpenBepInExDocs() => _shell.OpenUrl(new Uri("https://docs.bepinex.dev/"));

    [RelayCommand]
    private void OpenCreateMods() =>
        _shell.OpenUrl(new Uri("https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/index.html"));

    [RelayCommand]
    private void OpenLogs() => _shell.OpenFolder(_paths.LogsRoot);
}
