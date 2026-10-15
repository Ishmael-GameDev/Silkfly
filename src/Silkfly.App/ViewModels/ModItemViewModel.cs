using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Silkfly.App.Services;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Models;
using Silkfly.Core.Operations;
using Silkfly.Core.Profiles;
using Silkfly.Core.Thunderstore;

namespace Silkfly.App.ViewModels;

public sealed record ModItemContext(
    ILocalizer Localizer,
    IShellService Shell,
    IClipboardService Clipboard,
    IDialogService Dialogs,
    IIconCache Icons,
    IThunderstoreClient Thunderstore,
    IOperationQueue Queue,
    IToastService Toasts,
    Func<PackageId, Package?> Find,
    Func<PackageId, bool> IsInstalled,
    Func<Profile?> ActiveProfile,
    Action<PackageId> ShowPackage);

public sealed record DependencyItem(PackageId Id, string Text, bool InCatalog, bool Installed);

public sealed record ModDetails(ModItemViewModel Mod);

public sealed record BusyActions(ModItemViewModel Mod);

public sealed record InstalledActions(ModItemViewModel Mod);

public sealed record InstallActions(ModItemViewModel Mod);

public sealed record VersionOption(string Version, string Text, bool IsInstalled, System.Windows.Input.ICommand Command);

public sealed partial class ModItemViewModel : ObservableObject, Controls.IExpandableItem
{
    private readonly ModItemContext _ctx;
    private Bitmap? _icon;
    private bool _iconRequested;
    private bool _applyingState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    private bool _isExpanded;

    private ModDetails? _details;

    public ModDetails? Details => IsExpanded ? _details ??= new ModDetails(this) : null;

    [ObservableProperty]
    private bool _linkCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(NotInstalled), nameof(InstalledVersion), nameof(VersionText), nameof(IsManual), nameof(ShowInstall), nameof(Actions))]
    private InstalledMod? _installed;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdate), nameof(VersionText))]
    private bool _isOutdated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReinstall))]
    private bool _isBroken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(ShowInstall), nameof(ShowUpdate), nameof(ShowReinstall), nameof(ShowInstalledActions), nameof(Actions))]
    private OperationProgress? _operation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _lastError;

    [ObservableProperty]
    private IReadOnlyList<string> _dependents = [];

    public ModItemViewModel(Package package, bool isLocal, ModItemContext ctx)
    {
        Package = package;
        IsLocal = isLocal;
        _ctx = ctx;
    }

    public Package Package { get; private set; }

    public bool IsLocal { get; private set; }

    public PackageId Id => Package.Id;

    public string Name => Package.DisplayName;

    public string Author => Package.Owner;

    public string Description => Package.Latest.Description;

    public string LatestVersion => Package.Latest.Version.ToString();

    public IReadOnlyList<string> Categories => Package.Categories;

    public bool IsDeprecated => Package.IsDeprecated;

    public bool IsNsfw => Package.HasNsfwContent;

    public bool HasWebsite => Package.Latest.WebsiteUrl is not null;

    public bool IsInstalled => Installed is not null;

    public bool NotInstalled => Installed is null;

    public bool IsManual => Installed?.Source is ModSource.ManualDll or ModSource.ManualZip;

    public bool IsIdle => Operation is null;

    private BusyActions? _busyActions;
    private InstalledActions? _installedActions;
    private InstallActions? _installActions;

    public object? Actions =>
        !IsIdle ? _busyActions ??= new BusyActions(this)
        : IsInstalled ? _installedActions ??= new InstalledActions(this)
        : !IsLocal ? _installActions ??= new InstallActions(this)
        : null;

    public bool HasError => LastError is not null;

    public bool ShowInstall => IsIdle && NotInstalled && !IsLocal;

    public bool ShowUpdate => IsIdle && IsOutdated;

    public bool ShowReinstall => IsIdle && IsBroken && !IsLocal;

    public bool ShowInstalledActions => IsIdle && IsInstalled;

    public bool HasDependents => Dependents.Count > 0;

    public string? InstalledVersion => Installed?.Version.ToString();

    public string VersionText => Installed is null
        ? LatestVersion
        : IsOutdated ? $"{Installed.Version} → {LatestVersion}" : Installed.Version.ToString();

    public double OperationFraction => Operation?.Fraction ?? 0;

    public string OperationText => Operation?.Phase switch
    {
        OperationPhase.Downloading => _ctx.Localizer["Mod_Phase_Downloading"],
        OperationPhase.Applying => _ctx.Localizer["Mod_Phase_Applying"],
        _ => _ctx.Localizer["Mod_Phase_Queued"],
    };

    public bool CanCancel => Operation?.Phase is OperationPhase.Queued or OperationPhase.Downloading;

    public string DownloadsText => _ctx.Localizer.Format("Mod_Downloads", Package.TotalDownloads.ToString("N0", _ctx.Localizer.Culture));

    public string RatingText => _ctx.Localizer.Format("Mod_Rating", Package.RatingScore);

    public string UpdatedText => _ctx.Localizer.Format("Mod_UpdatedOn", Package.DateUpdated.LocalDateTime.ToString("d", _ctx.Localizer.Culture));

    public IReadOnlyList<VersionOption> Versions => Package.Versions
        .Take(30)
        .Select(v => new VersionOption(v.Version.ToString(), _ctx.Localizer.Format("Mod_InstallVersion", v.Version),
            Installed?.Version.SameAs(v.Version) == true, InstallVersionCommand))
        .ToList();

    public IReadOnlyList<DependencyItem> DependencyList =>
        (Installed is { } mod && Package.FindVersion(mod.Version) is { } installedVersion ? installedVersion : Package.Latest).Dependencies
            .Where(d => !d.IsLoaderPack)
            .Select(d => new DependencyItem(d.Id, $"{d.Id.DisplayName} {d.Version}", _ctx.Find(d.Id) is not null, _ctx.IsInstalled(d.Id)))
            .ToList();

    public bool HasDependencies => DependencyList.Count > 0;

    public Bitmap? Icon
    {
        get
        {
            RequestIcon();
            return _icon;
        }
    }

    public void RequestIcon()
    {
        if (_iconRequested)
            return;
        _iconRequested = true;
        _ = LoadIconAsync();
    }

    public bool HasIcon => _icon is not null;

    public void Update(Package package, bool isLocal)
    {
        var iconChanged = !package.Latest.Version.SameAs(Package.Latest.Version);
        Package = package;
        IsLocal = isLocal;
        if (iconChanged && _iconRequested)
        {
            _iconRequested = false;
            RequestIcon();
        }
        OnPropertyChanged(string.Empty);
    }

    public void ApplyState(InstalledMod? installed, bool outdated, bool broken, IReadOnlyList<string> dependents)
    {
        _applyingState = true;
        Installed = installed;
        IsEnabled = installed?.Enabled ?? false;
        IsOutdated = outdated;
        IsBroken = broken;
        Dependents = dependents;
        _applyingState = false;
        OnPropertyChanged(nameof(DependencyList));
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(HasDependents));
        OnPropertyChanged(nameof(Versions));
    }

    public void ApplyOperation(OperationProgress? progress)
    {
        Operation = progress;
        OnPropertyChanged(nameof(OperationFraction));
        OnPropertyChanged(nameof(OperationText));
        OnPropertyChanged(nameof(CanCancel));
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(DownloadsText));
        OnPropertyChanged(nameof(RatingText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(OperationText));
        OnPropertyChanged(nameof(Versions));
    }

    public override string ToString() => Name;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_applyingState || Installed is null || Installed.Enabled == value)
            return;
        Enqueue(p => _ctx.Queue.EnqueueSetEnabled(p, Id, value));
    }

    private async Task LoadIconAsync()
    {
        var icon = await _ctx.Icons.GetAsync(Package.Latest);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(HasIcon));
        });
    }

    private void Enqueue(Func<Profile, Guid> start)
    {
        if (_ctx.ActiveProfile() is not { } profile)
        {
            _ctx.Toasts.Show(_ctx.Localizer["Error_NoProfile"], ToastKind.Warning);
            return;
        }

        LastError = null;
        start(profile);
    }

    [RelayCommand]
    private void Install() => Enqueue(p => _ctx.Queue.EnqueueInstall(p, Id));

    [RelayCommand]
    private void InstallVersion(VersionOption option) =>
        Enqueue(p => _ctx.Queue.EnqueueInstall(p, Id, Core.Models.Versions.Parse(option.Version)));

    [RelayCommand]
    private void Update() => Enqueue(p => _ctx.Queue.EnqueueUpdate(p, Id));

    [RelayCommand]
    private void Reinstall() => Enqueue(p => _ctx.Queue.EnqueueInstall(p, Id, Installed?.Version));

    [RelayCommand]
    private void Uninstall() => Enqueue(p => _ctx.Queue.EnqueueUninstall(p, Id));

    [RelayCommand]
    private void Retry()
    {
        if (IsInstalled && IsOutdated)
            Update();
        else if (IsInstalled)
            Reinstall();
        else
            Install();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (Operation is { } op)
            _ctx.Queue.Cancel(op.OperationId);
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void OpenPage() => _ctx.Shell.OpenUrl(Package.PackageUrl);

    [RelayCommand]
    private void OpenWebsite()
    {
        if (Package.Latest.WebsiteUrl is { } url)
            _ctx.Shell.OpenUrl(url);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (_ctx.ActiveProfile() is not { } profile || Installed is null)
            return;
        var folder = Path.Combine(Installed.Enabled ? profile.RootPath : Path.Combine(profile.RootPath, "disabled"),
            "BepInEx", "plugins", Id.FullName);
        _ctx.Shell.OpenFolder(Directory.Exists(folder) ? folder : profile.PluginsPath);
    }

    [RelayCommand]
    private void OpenConfigs()
    {
        if (_ctx.ActiveProfile() is not { } profile || Installed is null)
            return;

        var configs = ModConfigs.Find(LocalFileSystem, profile, Installed);
        if (configs.Count == 1)
            _ctx.Shell.OpenFile(configs[0]);
        else if (configs.Count > 1)
            _ctx.Shell.RevealInFileManager(configs[0]);
        else
            _ctx.Shell.OpenFolder(profile.ConfigPath);
    }

    private static readonly System.IO.Abstractions.IFileSystem LocalFileSystem = new System.IO.Abstractions.FileSystem();

    [RelayCommand]
    private async Task CopyLinkAsync()
    {
        await _ctx.Clipboard.SetTextAsync(Package.PackageUrl.AbsoluteUri);
        LinkCopied = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        LinkCopied = false;
    }

    [RelayCommand]
    private Task ShowReadmeAsync()
    {
        var version = Installed?.Version ?? Package.Latest.Version;
        return _ctx.Dialogs.ShowMarkdownAsync(
            Name, $"README · {version}", ct => _ctx.Thunderstore.GetReadmeAsync(Id, version, ct), "README");
    }

    [RelayCommand]
    private Task ShowChangelogAsync()
    {
        var version = Package.Latest.Version;
        return _ctx.Dialogs.ShowMarkdownAsync(
            Name, $"CHANGELOG · {version}", ct => _ctx.Thunderstore.GetChangelogAsync(Id, version, ct), "CHANGELOG");
    }

    [RelayCommand]
    private void OpenDependency(DependencyItem dependency) => _ctx.ShowPackage(dependency.Id);
}

public sealed partial class LinkSuggestion(Package package, Action<Package> link)
{
    public string Text { get; } = $"{package.Id.DisplayName} · {package.Owner} · {package.Latest.Version}";

    [RelayCommand]
    private void Link() => link(package);
}

public sealed partial class UnmanagedItemViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<Package>> _catalog;
    private readonly Action<string> _searchCatalog;
    private IReadOnlyList<LinkSuggestion>? _suggestions;

    private readonly Core.Install.UnmanagedItem _item;
    private readonly IOperationQueue _queue;
    private readonly Func<Profile?> _activeProfile;
    private readonly IShellService _shell;
    private bool _applying;

    [ObservableProperty]
    private bool _isEnabled;

    public UnmanagedItemViewModel(
        Core.Install.UnmanagedItem item,
        IOperationQueue queue,
        Func<Profile?> activeProfile,
        IShellService shell,
        Func<IReadOnlyList<Package>> catalog,
        Action<string> searchCatalog)
    {
        _catalog = catalog;
        _searchCatalog = searchCatalog;
        _item = item;
        _queue = queue;
        _activeProfile = activeProfile;
        _shell = shell;
        _applying = true;
        IsEnabled = !item.LooksDisabled;
        _applying = false;
    }

    public string Name => _item.Name;

    public string RelativePath => _item.RelativePath;

    public IReadOnlyList<LinkSuggestion> Suggestions => _suggestions ??=
        UnmanagedMatcher.Suggest(_item, _catalog()).Select(p => new LinkSuggestion(p, Link)).ToList();

    public bool HasNoSuggestions => Suggestions.Count == 0;

    private void Link(Package package)
    {
        if (_activeProfile() is { } profile)
            _queue.EnqueueLinkUnmanaged(profile, _item, package.Id);
    }

    [RelayCommand]
    private void SearchCatalog() =>
        _searchCatalog(Path.GetFileNameWithoutExtension(Name.Replace(".disabled", string.Empty, StringComparison.OrdinalIgnoreCase)));

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_applying && _activeProfile() is { } profile)
            _queue.EnqueueSetUnmanagedEnabled(profile, _item, value);
    }

    [RelayCommand]
    private void Remove()
    {
        if (_activeProfile() is { } profile)
            _queue.EnqueueRemoveUnmanaged(profile, _item);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (_activeProfile() is not { } profile)
            return;
        var full = Path.Combine(profile.RootPath, _item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (_item.IsDirectory)
            _shell.OpenFolder(full);
        else
            _shell.RevealInFileManager(full);
    }

    public override string ToString() => Name;
}
