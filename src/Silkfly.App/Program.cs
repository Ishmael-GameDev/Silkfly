using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Silkfly.Core.Infrastructure;
using Velopack;

namespace Silkfly.App;

internal static class Program
{
    public static LoggingLevelSwitch LogLevel { get; } = new(LogEventLevel.Information);

    public static LoggingLevelSwitch HttpLogLevel { get; } = new(LogEventLevel.Warning);

    public static void SetVerboseLogging(bool verbose)
    {
        LogLevel.MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;
        HttpLogLevel.MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Warning;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        // must run first (Velopack hooks)
        VelopackApp.Build().Run();

        ConfigureLogging();
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Silkfly terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Silkfly/Assets/Fonts#Noto Sans" })
            .LogToTrace()
            .AfterSetup(_ =>
            {
                Avalonia.Logging.Logger.Sink = new Services.AvaloniaLogSink();
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    Log.Error(e.Exception, "Unhandled exception on the UI thread");
                    e.Handled = true;
                    (Application.Current as App)?.ReportUnexpected(e.Exception);
                };
            });

    private static void ConfigureLogging()
    {
        var logsRoot = new AppPaths(AppPaths.DefaultDataRoot()).LogsRoot;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LogLevel)
            .MinimumLevel.Override("System.Net.Http", HttpLogLevel)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logsRoot, "silkfly-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
            (Application.Current as App)?.ReportUnexpected(e.Exception.InnerExceptions.Count == 1 ? e.Exception.InnerException! : e.Exception);
        };
    }
}
