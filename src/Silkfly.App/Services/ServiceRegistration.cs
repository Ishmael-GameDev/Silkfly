using System.IO.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Silkfly.App.Localization;
using Silkfly.App.ViewModels;
using Silkfly.Core.Catalog;
using Silkfly.Core.Dependencies;
using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Launch;
using Silkfly.Core.Loader;
using Silkfly.Core.News;
using Silkfly.Core.Operations;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;
using Silkfly.Core.Thunderstore;
using Silkfly.Core.Updates;

namespace Silkfly.App.Services;

public static class ServiceRegistration
{
    public static IServiceCollection AddSilkfly(this IServiceCollection services, IAppPaths paths)
    {
        services.AddLogging(builder => builder.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddSerilog(dispose: false));
        services.AddTransient(typeof(Lazy<>), typeof(LazyService<>));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton(paths);
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILocalizer>(Localizer.Instance);
        services.AddSingleton<ISystemEnvironment, SystemEnvironment>();

        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IFilePicker, FilePicker>();
        services.AddSingleton<IIconCache, IconCache>();
        services.AddSingleton<UiScaleLimits>();

        services.AddHttpClient(ThunderstoreClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://thunderstore.io/");
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Silkfly", AppInfo.Version));
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"(+{AppInfo.RepositoryUrl})"));
                client.Timeout = Timeout.InfiniteTimeSpan; // timeouts are per request
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            });
        services.AddSingleton<IThunderstoreClient, ThunderstoreClient>();
        services.AddSingleton<CatalogCache>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IPackageCache, PackageCache>();

        services.AddHttpClient(NewsService.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Silkfly", AppInfo.Version));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"(+{AppInfo.RepositoryUrl})"));
        });
        services.AddSingleton(new NewsOptions(new Uri(AppInfo.NewsFeedUrl)));
        services.AddSingleton<INewsService, NewsService>();
        services.AddSingleton<IAppUpdateService, VelopackUpdateService>();

        services.AddSingleton<IGameLocator, GameLocator>();
        services.AddSingleton<ILoaderService, LoaderService>();
        services.AddSingleton<IProfileLockStore, ProfileLockStore>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ILaunchService, LaunchService>();

        services.AddSingleton<IPackageLayout, PackageLayout>();
        services.AddSingleton<IDependencyResolver, DependencyResolver>();
        services.AddSingleton<IModInstaller, ModInstaller>();
        services.AddSingleton<IModStateService, ModStateService>();
        services.AddSingleton<IUserPrompts, UserPrompts>();
        services.AddSingleton<IOperationQueue, OperationQueue>();
        services.AddSingleton<IProfileTransfer, ProfileTransfer>();
        services.AddSingleton<ProfileSharing>();
        services.AddSingleton<ProfileIcons>();

        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<InfoViewModel>();
        services.AddSingleton<ModListViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AppUpdateViewModel>();

        return services;
    }

    private sealed class LazyService<T>(IServiceProvider provider) : Lazy<T>(provider.GetRequiredService<T>)
        where T : notnull;
}
