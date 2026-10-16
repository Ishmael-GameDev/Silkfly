using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Silkfly.App.Localization;
using Silkfly.App.Services;
using Silkfly.Core.Catalog;
using Silkfly.Core.Dependencies;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Models;
using Silkfly.Core.Operations;
using Silkfly.Core.Settings;
using Silkfly.Core.Thunderstore;

namespace Silkfly.App.ViewModels;

public sealed partial class CategoryOption(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class SortOption(SortOrder value, string key, ILocalizer localizer) : ObservableObject
{
    public SortOrder Value { get; } = value;

    public string DisplayName => localizer[key];

    public void Refresh() => OnPropertyChanged(nameof(DisplayName));
}

public sealed partial class ModListViewModel : ObservableObject
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(150);

    private readonly IDialogService _dialogs;
    private readonly ICatalogService _catalog;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly SessionViewModel _session;
    private readonly IOperationQueue _queue;
    private readonly IModStateService _states;
    private readonly IModInstaller _installer;
    private readonly IDependencyResolver _resolver;
    private readonly IFilePicker _picker;
    private readonly IShellService _shell;
    private readonly IToastService _toasts;
    private readonly ProfileSharing _sharing;
    private readonly Silkfly.Core.Game.ISystemEnvironment _environment;
    private readonly ILogger<ModListViewModel> _logger;
    private readonly ModItemContext _itemContext;
    private readonly Dictionary<PackageId, ModItemViewModel> _cards = new();
    private readonly Dictionary<PackageId, string> _lastErrors = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _clock;
    private ProfileModsSnapshot _snapshot = ProfileModsSnapshot.Empty;
    private List<UnmanagedItemViewModel> _unmanaged = [];

    private IReadOnlyList<Package> _packages = [];
    private bool _suspendApply;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ModFilter _filter = ModFilter.All;

    [ObservableProperty]
    private SortOption _selectedSort;

    [ObservableProperty]
    private string? _selectedAuthor;

    [ObservableProperty]
    private bool _showDeprecated;

    [ObservableProperty]
    private bool _showNsfw;

    [ObservableProperty]
    private IReadOnlyList<object> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNothingFound))]
    private int _shownCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSkeletons), nameof(SkeletonItems), nameof(ShowUnavailable), nameof(ShowNothingFound))]
    private bool _isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSkeletons), nameof(SkeletonItems), nameof(ShowUnavailable), nameof(ShowNothingFound))]
    private bool _hasCatalog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSkeletons), nameof(SkeletonItems), nameof(ShowUnavailable), nameof(ShowNothingFound))]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _offlineText;

    [ObservableProperty]
    private string _updatedText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    private bool _hasOutdated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallAllCommand))]
    private bool _hasInstalled;

    [ObservableProperty]
    private bool _isDropTarget;

    [ObservableProperty]
    private bool _whatsNewMonth;

    [ObservableProperty]
    private bool _whatsNewIncludesNew = true;

    [ObservableProperty]
    private bool _whatsNewIncludesUpdated;

    public bool IsWhatsNew => Filter == ModFilter.WhatsNew;

    public bool WhatsNewWeek
    {
        get => !WhatsNewMonth;
        set => WhatsNewMonth = !value;
    }

    partial void OnWhatsNewMonthChanged(bool value)
    {
        OnPropertyChanged(nameof(WhatsNewWeek));
        CollapseAndApply();
    }

    partial void OnWhatsNewIncludesNewChanged(bool value) => CollapseAndApply();

    partial void OnWhatsNewIncludesUpdatedChanged(bool value) => CollapseAndApply();

    public ModListViewModel(
        ICatalogService catalog,
        ISettingsService settings,
        ILocalizer localizer,
        IShellService shell,
        IClipboardService clipboard,
        IDialogService dialogs,
        IIconCache icons,
        IThunderstoreClient thunderstore,
        TimeProvider time,
        SessionViewModel session,
        IOperationQueue queue,
        IModStateService states,
        IModInstaller installer,
        IDependencyResolver resolver,
        IFilePicker picker,
        IToastService toasts,
        ProfileSharing sharing,
        Silkfly.Core.Game.ISystemEnvironment environment,
        ILogger<ModListViewModel> logger)
    {
        _catalog = catalog;
        _dialogs = dialogs;
        _settings = settings;
        _localizer = localizer;
        _time = time;
        _session = session;
        _queue = queue;
        _states = states;
        _installer = installer;
        _resolver = resolver;
        _picker = picker;
        _shell = shell;
        _toasts = toasts;
        _sharing = sharing;
        _environment = environment;
        _logger = logger;
        _itemContext = new ModItemContext(localizer, shell, clipboard, dialogs, icons, thunderstore, queue, toasts,
            catalog.Find, id => _snapshot.Installed.ContainsKey(id), () => session.ActiveProfile?.Profile, ShowPackage);

        SortOptions =
        [
            new SortOption(SortOrder.Name, "Mods_Sort_Name", localizer),
            new SortOption(SortOrder.Downloads, "Mods_Sort_Downloads", localizer),
            new SortOption(SortOrder.Rating, "Mods_Sort_Rating", localizer),
            new SortOption(SortOrder.Updated, "Mods_Sort_Updated", localizer),
            new SortOption(SortOrder.Created, "Mods_Sort_Created", localizer),
        ];
        _selectedSort = SortOptions[0];
        _showDeprecated = settings.Current.ShowDeprecated;
        _showNsfw = settings.Current.ShowNsfw;

        _debounce = new DispatcherTimer { Interval = SearchDebounce };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Apply();
        };

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => UpdateStatusTexts();
        _clock.Start();

        catalog.CatalogChanged += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        settings.Changed += (_, s) => Dispatcher.UIThread.Post(() =>
        {
            ShowDeprecated = s.ShowDeprecated;
            ShowNsfw = s.ShowNsfw;
            Apply();
        });
        catalog.RefreshStateChanged += (_, _) => Dispatcher.UIThread.Post(OnRefreshStateChanged);
        queue.Progress += (_, p) => Dispatcher.UIThread.Post(() => OnOperationProgress(p));
        queue.Completed += (_, c) => Dispatcher.UIThread.Post(() => OnOperationCompleted(c));
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.ActiveProfile))
                Dispatcher.UIThread.Post(Rebuild);
        };
        localizer.CultureChanged += (_, _) =>
        {
            foreach (var option in SortOptions)
                option.Refresh();
            foreach (var card in _cards.Values)
                card.RefreshTexts();
            UpdateStatusTexts();
        };
    }

    public IReadOnlyList<SortOption> SortOptions { get; }

    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public ObservableCollection<string> Authors { get; } = [];

    public IReadOnlyList<int>? SkeletonItems => ShowSkeletons ? Placeholders.Skeletons : null;

    public bool ShowSkeletons => !HasCatalog && (!IsLoaded || IsRefreshing) && _snapshot.Installed.Count == 0;

    public bool ShowUnavailable => !HasCatalog && IsLoaded && !IsRefreshing && _snapshot.Installed.Count == 0;

    public bool ShowNothingFound => IsLoaded && (HasCatalog || _snapshot.Installed.Count > 0) && ShownCount == 0;

    public bool HasAdvancedFilters =>
        Categories.Any(c => c.IsSelected) || SelectedAuthor is not null || SelectedSort.Value != SortOrder.Name;

    public bool HasProfile => _session.ActiveProfile is not null;

    public bool AnyEnabled => _snapshot.Installed.Values.Any(m => m.Enabled);

    public event EventHandler<ModItemViewModel>? ScrollToRequested;

    public event EventHandler? Leaving;

    public event EventHandler? FiltersReset;

    public object? ScrollAnchor { get; set; }

    public double ScrollAnchorDelta { get; set; }

    public double ScrollOffset { get; set; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await _catalog.LoadCachedAsync(ct);
            await _catalog.RefreshAsync(force: false, ct);
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoaded = true);
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => _catalog.RefreshAsync(force: true, CancellationToken.None);

    [RelayCommand]
    private void ResetFilters()
    {
        _suspendApply = true;
        SearchText = string.Empty;
        Filter = ModFilter.All;
        SelectedAuthor = null;
        SelectedSort = SortOptions[0];
        foreach (var category in Categories)
            category.IsSelected = false;
        _suspendApply = false;
        Apply();
        FiltersReset?.Invoke(this, EventArgs.Empty);
    }

    private bool CanUninstallAll() => HasInstalled;

    [RelayCommand(CanExecute = nameof(CanUninstallAll))]
    private async Task UninstallAllAsync()
    {
        if (_session.ActiveProfile?.Profile is not { } profile)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer["Rail_UninstallAll_Title"],
            _localizer.Format("Rail_UninstallAll_Text", _snapshot.Installed.Count, profile.Name),
            _localizer["Rail_UninstallAll_Confirm"],
            danger: true);
        if (confirmed)
            _queue.EnqueueUninstallAll(profile);
    }

    private bool CanUpdateAll() => HasOutdated;

    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private void UpdateAll()
    {
        if (_session.ActiveProfile?.Profile is { } profile)
            _queue.EnqueueUpdateAll(profile);
    }

    [RelayCommand]
    private void ToggleAll()
    {
        if (_session.ActiveProfile?.Profile is { } profile)
            _queue.EnqueueSetAllEnabled(profile, enabled: !AnyEnabled);
    }

    [RelayCommand]
    private async Task ManualInstallAsync()
    {
        var file = await _picker.PickFileAsync(_localizer["Manual_PickTitle"],
            [new FilePickerFileType(_localizer["Manual_FileTypes"]) { Patterns = ["*.zip", "*.dll"] }]);
        if (file is not null)
            InstallFiles([file]);
    }

    public void InstallFiles(IEnumerable<string> paths)
    {
        if (_session.ActiveProfile?.Profile is not { } profile)
        {
            _toasts.Show(_localizer["Error_NoProfile"], ToastKind.Warning);
            return;
        }

        foreach (var path in paths)
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                _queue.EnqueueManualInstall(profile, path);
            else
                _toasts.Show(_localizer["Manual_Unsupported"], ToastKind.Warning);
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (_session.ActiveProfile?.Profile is { } profile)
            await _sharing.ExportToFileAsync(profile);
    }

    [RelayCommand]
    private void OpenSavesFolder()
    {
        var candidates = Silkfly.Core.Game.GameInfo.SaveFolderCandidates(_environment.HostPlatform, _environment.HomeDirectory);
        var folder = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        _shell.OpenFolder(folder);
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (_session.ActiveProfile?.Profile is { } profile)
            _shell.OpenFolder(profile.PluginsPath);
    }

    [RelayCommand]
    private void OpenProfileFolder()
    {
        if (_session.ActiveProfile?.Profile is { } profile)
            _shell.OpenFolder(profile.RootPath);
    }

    [RelayCommand]
    private void OpenGameLog()
    {
        if (_session.ActiveProfile?.Profile is not { } profile)
            return;
        if (File.Exists(profile.LogPath))
            _shell.OpenFile(profile.LogPath);
        else
            _shell.OpenFolder(profile.BepInExPath);
    }

    public void ShowPackage(PackageId id)
    {
        if (!_cards.TryGetValue(id, out var card))
            return;

        CollapseAll();
        if (!Items.Contains(card))
        {
            ResetFilters();
            if (!Items.Contains(card) && (card.IsDeprecated || card.IsNsfw))
            {
                ShowDeprecated |= card.IsDeprecated;
                ShowNsfw |= card.IsNsfw;
            }
        }

        card.IsExpanded = true;
        ScrollToRequested?.Invoke(this, card);
    }

    private void SearchCatalogFor(string text)
    {
        Filter = ModFilter.All;
        SearchText = text;
    }

    public void Leave()
    {
        Leaving?.Invoke(this, EventArgs.Empty);
        CollapseAll();
    }

    public void CollapseAll()
    {
        foreach (var card in _cards.Values)
            card.IsExpanded = false;
    }

    private void CollapseAndApply()
    {
        CollapseAll();
        Apply();
    }

    partial void OnSearchTextChanged(string value)
    {
        CollapseAll();
        _debounce.Stop();
        _debounce.Start();
    }

    partial void OnFilterChanged(ModFilter value)
    {
        OnPropertyChanged(nameof(IsWhatsNew));
        CollapseAndApply();
    }

    partial void OnSelectedSortChanged(SortOption value) => CollapseAndApply();

    partial void OnSelectedAuthorChanged(string? value) => CollapseAndApply();

    partial void OnShowDeprecatedChanged(bool value)
    {
        CollapseAndApply();
        _ = _settings.UpdateAsync(s => s with { ShowDeprecated = value });
    }

    partial void OnShowNsfwChanged(bool value)
    {
        CollapseAndApply();
        _ = _settings.UpdateAsync(s => s with { ShowNsfw = value });
    }

    private void Rebuild()
    {
        var profile = _session.ActiveProfile?.Profile;
        try
        {
            _snapshot = profile is null ? ProfileModsSnapshot.Empty : _states.Load(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SilkflyException)
        {
            _logger.LogError(ex, "Could not read profile state");
            _snapshot = ProfileModsSnapshot.Empty;
        }

        var catalogSnapshot = _catalog.Current;
        var listable = catalogSnapshot.Listable.ToList();
        var local = _snapshot.LocalPackages;

        foreach (var package in listable)
            Upsert(package, isLocal: false);
        foreach (var package in local)
            Upsert(package, isLocal: true);

        var known = listable.Select(p => p.Id).Concat(local.Select(p => p.Id)).ToHashSet();
        foreach (var stale in _cards.Keys.Where(k => !known.Contains(k)).ToList())
            _cards.Remove(stale);

        ApplyStates();
        _unmanaged = _snapshot.Unmanaged
            .Select(u => new UnmanagedItemViewModel(u, _queue, () => _session.ActiveProfile?.Profile, _shell, () => _packages, SearchCatalogFor))
            .ToList();

        _packages = listable.Concat(local).ToList();
        RebuildFilters(_packages);
        HasCatalog = catalogSnapshot.Source != CatalogSource.Empty;
        TotalCount = listable.Count + local.Count;
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(AnyEnabled));
        Apply();
        UpdateStatusTexts();
    }

    private void Upsert(Package package, bool isLocal)
    {
        if (_cards.TryGetValue(package.Id, out var card))
            card.Update(package, isLocal);
        else
            _cards[package.Id] = new ModItemViewModel(package, isLocal, _itemContext);
    }

    private void ApplyStates()
    {
        var installed = _snapshot.Installed.Values.ToList();
        foreach (var card in _cards.Values)
        {
            var mod = _snapshot.Find(card.Id);
            var dependents = mod is null
                ? []
                : _resolver.FindDependents(installed, card.Id, _catalog.FindVersion).Select(d => d.Id.DisplayName).ToList();
            card.ApplyState(mod, mod is not null && _states.IsOutdated(mod), _snapshot.Broken.Contains(card.Id), dependents);
            card.LastError = _lastErrors.GetValueOrDefault(card.Id);
            card.ApplyOperation(_queue.Active.FirstOrDefault(o => o.Target == card.Id));
        }

        HasOutdated = installed.Any(_states.IsOutdated);
        HasInstalled = _snapshot.Installed.Count > 0;
    }

    private void RebuildFilters(IReadOnlyList<Package> packages)
    {
        var selected = Categories.Where(c => c.IsSelected).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var category in Categories)
            category.PropertyChanged -= OnCategoryChanged;
        Categories.Clear();
        foreach (var name in packages.SelectMany(p => p.Categories).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var option = new CategoryOption(name) { IsSelected = selected.Contains(name) };
            option.PropertyChanged += OnCategoryChanged;
            Categories.Add(option);
        }

        var authors = packages.Select(p => p.Owner).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (!authors.SequenceEqual(Authors))
        {
            Authors.Clear();
            foreach (var author in authors)
                Authors.Add(author);
        }
    }

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategoryOption.IsSelected))
            CollapseAndApply();
    }

    private void OnRefreshStateChanged()
    {
        IsRefreshing = _catalog.IsRefreshing;
        UpdateStatusTexts();
    }

    private void OnOperationProgress(OperationProgress progress)
    {
        if (progress.Target is { } target && _cards.TryGetValue(target, out var card))
            card.ApplyOperation(progress.Phase is OperationPhase.Done or OperationPhase.Failed or OperationPhase.Canceled ? null : progress);
    }

    private void OnOperationCompleted(OperationCompleted result)
    {
        if (result.Target is { } target)
        {
            if (result.Error is null or OperationCanceledException)
                _lastErrors.Remove(target);
            else
                _lastErrors[target] = DescribeError(result.Error);
        }

        if (result.Error is null)
        {
            var name = result.Target?.DisplayName ?? string.Empty;
            var key = result.Kind switch
            {
                OperationKind.Install when result.Target is null => "Op_ImportDone",
                OperationKind.Install => "Op_Installed",
                OperationKind.Update => "Op_Updated",
                OperationKind.UpdateAll => "Op_UpdatedAll",
                OperationKind.Uninstall when result.Target is not null => "Op_Uninstalled",
                OperationKind.ManualInstall => "Op_ManualInstalled",
                _ => null,
            };
            if (key is not null)
                _toasts.Show(_localizer.Format(key, name), ToastKind.Success);
        }
        else if (result.Error is PartialImportException partial)
        {
            _toasts.Show(DescribeError(partial), ToastKind.Warning);
        }
        else if (result.Error is not OperationCanceledException)
        {
            _toasts.ShowError("Op_Failed", result.Error);
        }

        if (result.Profile.Id == _session.ActiveProfile?.Profile.Id)
            Rebuild();
        _session.RefreshProfileCounts();
    }

    private string DescribeError(Exception error) =>
        error is SilkflyException domain ? _localizer.Format(domain.MessageKey, [.. domain.Args]) : error.Message;

    private void Apply()
    {
        if (_suspendApply)
            return;

        var whatsNew = Filter == ModFilter.WhatsNew;
        var now = _time.GetUtcNow();
        var query = new CatalogQuery
        {
            Text = whatsNew ? null : SearchText,
            Filter = Filter,
            Categories = whatsNew
                ? new HashSet<string>()
                : Categories.Where(c => c.IsSelected).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Author = whatsNew ? null : SelectedAuthor,
            Sort = SelectedSort.Value,
            IncludeDeprecated = ShowDeprecated,
            IncludeNsfw = ShowNsfw,
            StateOf = id => _snapshot.Find(id) is { } mod
                ? new ModQueryState(true, mod.Enabled, _states.IsOutdated(mod))
                : default,
            Now = now,
            WhatsNewDays = WhatsNewMonth ? 30 : 7,
            WhatsNewIncludesNew = WhatsNewIncludesNew,
            WhatsNewIncludesUpdated = WhatsNewIncludesUpdated,
        };

        var shown = CatalogSearch.Query(_packages, query)
            .Select(p => _cards.GetValueOrDefault(p.Id))
            .OfType<object>()
            .ToList();

        if (Filter is ModFilter.All or ModFilter.Installed or ModFilter.Enabled)
        {
            var words = whatsNew ? [] : SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown.AddRange(_unmanaged.Where(u =>
                (Filter != ModFilter.Enabled || u.IsEnabled) &&
                words.All(w => u.Name.Contains(w, StringComparison.OrdinalIgnoreCase))));
        }

        if (!shown.SequenceEqual(Items))
            Items = shown;
        var count = shown.Count;
        ShownCount = count;
        OnPropertyChanged(nameof(HasAdvancedFilters));
    }
    private void UpdateStatusTexts()
    {
        var snapshot = _catalog.Current;
        var hasData = snapshot.Source != CatalogSource.Empty;

        UpdatedText = IsRefreshing
            ? _localizer["Mods_Refreshing"]
            : hasData
                ? _localizer.Format("Mods_Updated", RelativeTime.Format(_localizer, _time.GetUtcNow() - snapshot.FetchedAt))
                : string.Empty;

        OfflineText = hasData && _catalog.LastRefreshError is not null && !IsRefreshing
            ? _localizer.Format("Mods_Offline", snapshot.FetchedAt.LocalDateTime.ToString("g", _localizer.Culture))
            : null;
    }
}
