// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader;

public partial class App
{
    private string logFile = Path.Combine(AppInfoHelper.GetAppPath(), "crash.log");
    private bool _isViewAtBoot;

    public App()
    {
        RenderOptions.ProcessRenderMode = RenderMode.Default;

        Bootstrap.OnBeforeStartup();
        _isViewAtBoot = Bootstrap.IsViewAtBoot;

        TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        File.AppendAllText(logFile, $"[{DateTime.Now}] UnhandledException: {ex}\n");
    }

    public void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        File.AppendAllText(logFile, $"[{DateTime.Now}] UnobservedTaskException: {e.Exception}\n");
        e.SetObserved();
    }

    private static readonly IHost _host = Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(c =>
        {
            c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty);
        })
        .ConfigureServices((context, services) =>
        {
            services.AddNavigationViewPageProvider();

            services.AddSingleton<NavigationPanelHostService>();
            services.AddSingleton<IHostedService>(ihsv => ihsv.GetRequiredService<NavigationPanelHostService>());

            services.AddHostedService<ApplicationHostService>();

            services.AddHostedService<PowerModeHostService>();

            services.AddSingleton<PowerModeService>();

            services.AddSingleton<DownloadConfigService>();
            services.AddSingleton<DownloadLauncherService>();
            services.AddSingleton<DownloadsChannelService>();

            services.AddSingleton<IWindow, MainWindow>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ISnackbarService, SnackbarService>();

            services.AddSingleton<UpdateService>();
            services.AddSingleton<UpdateHostService>();

            NavigationHandle.SetupPageViewModelPairs(services, "PDownloader.Views.Pages", "PDownloader.ViewModels.Pages");
            NavigationHandle.SetupPageViewModelPairs(services, "PDownloader.Views.PagesBottom", "PDownloader.ViewModels.PagesBottom");
        }).Build();

    public static IServiceProvider Services => _host.Services;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (_isViewAtBoot)
        {
            _host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        Bootstrap.OnStartup();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        if (_isViewAtBoot)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        Bootstrap.OnExit();

        if (_isViewAtBoot)
        {
            _host.Dispose();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) { }

    public static T GetRequiredService<T>()
        where T : class
    {
        return _host.Services.GetRequiredService<T>();
    }
}
