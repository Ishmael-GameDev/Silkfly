using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Silkfly.App.Services;
using Silkfly.App.ViewModels;
using Silkfly.App.Views;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Settings;

namespace Silkfly.App;

public sealed class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var paths = new AppPaths(
            AppPaths.DefaultDataRoot(),
            () => _services?.GetRequiredService<ISettingsService>().Current.ProfilesRoot);

        _services = new ServiceCollection().AddSilkfly(paths).BuildServiceProvider();

        var settings = _services.GetRequiredService<ISettingsService>();
        settings.Load();
        Program.SetVerboseLogging(settings.Current.VerboseLogging);
        settings.Changed += (_, s) => Program.SetVerboseLogging(s.VerboseLogging);
        _services.GetRequiredService<ILocalizer>().SetCulture(settings.Current.Language);
        WatchLockRepairs();

        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Silkfly {Version} started, data folder {DataRoot}", AppInfo.Version, paths.DataRoot);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = _services.GetRequiredService<ShellViewModel>();
            desktop.MainWindow = new MainWindow(settings, _services.GetRequiredService<UiScaleLimits>()) { DataContext = shell };
            desktop.Exit += (_, _) => _services.Dispose();

            if (settings.CorruptFileBackupPath is { } backup)
                _services.GetRequiredService<IToastService>().Show(
                    $"settings.json → {Path.GetFileName(backup)}", ToastKind.Warning);

            _ = StartAsync(shell, logger);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartAsync(ShellViewModel shell, ILogger logger)
    {
        var catalog = Guard(shell.Mods.InitializeAsync(), "Catalog initialization", logger);
        var session = Guard(shell.Session.InitializeAsync(), "Session initialization", logger);
        _ = Guard(shell.Info.LoadNewsAsync(), "Loading news", logger);
        _ = Guard(shell.Updates.CheckOnStartupAsync(), "Update check", logger);

        await session;
        if (shell.Session.NeedsOnboarding)
            shell.ShowOnboarding();

        await catalog;
    }

    private void WatchLockRepairs()
    {
        var localizer = _services!.GetRequiredService<ILocalizer>();
        var toasts = _services!.GetRequiredService<IToastService>();
        _services!.GetRequiredService<Silkfly.Core.Profiles.IProfileLockStore>().LockRepaired += (_, repair) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => toasts.Show(
                localizer[repair.FromBackup ? "Profile_LockRestored" : "Profile_LockReset"],
                ToastKind.Warning,
                $"{repair.ProfileRoot}{Environment.NewLine}{repair.CorruptCopy}"));
    }

    private bool _showingCrash;
    private DateTime _lastCrashDialog;

    public void ReportUnexpected(Exception exception)
    {
        if (_services is null)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (_showingCrash || DateTime.UtcNow - _lastCrashDialog < TimeSpan.FromSeconds(10))
                return;

            _showingCrash = true;
            try
            {
                await _services.GetRequiredService<IDialogService>().ShowErrorAsync(
                    _services.GetRequiredService<ILocalizer>()["Error_Unexpected"], exception.ToString());
            }
            catch (Exception dialogError)
            {
                Serilog.Log.Error(dialogError, "Could not show the error dialog");
            }
            finally
            {
                _showingCrash = false;
                _lastCrashDialog = DateTime.UtcNow;
            }
        });
    }

    private static async Task Guard(Task task, string what, ILogger logger)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{What} failed", what);
        }
    }
}
