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

namespace PDownloader.Runner;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    private static readonly IHost _host;

    private static readonly string[] _args;

    static App()
    {
        _args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty); })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(RunnerConfig.ParseArgs(_args));
                services.AddSingleton<PowerModeService>();
                services.AddSingleton<ProgressWindowBehaviorSettingsService>();
                services.AddSingleton<DownloaderService>();

                // Establish the Core session and hydrate RunnerConfig before the
                // window is created, so no download metadata is needed on the
                // process command line.
                services.AddHostedService(sp =>
                    sp.GetRequiredService<DownloaderService>());
                // WPF window creation is dispatcher-owned and must not run as an
                // IHostedService. The Generic Host is allowed to resume hosted
                // service startup on a worker thread after asynchronous IPC work.
                services.AddSingleton<ApplicationHostService>();

                services.AddSingleton<Services.INavigationService, Services.NavigationService>();

                services.AddSingleton<IWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                services.AddSingleton<DownloaderPage>();
                services.AddSingleton<DownloaderViewModel>();

                services.AddSingleton<DownloaderProgressPage>();
                services.AddSingleton<DownloaderProgressViewModel>();
            }).Build();
    }

    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services
    {
        get { return _host.Services; }
    }

    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        await UserDataStore.InitializeAsync();
        await _host.StartAsync();

        TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();

        // This continuation belongs to the WPF Dispatcher. Keep all window/page
        // creation on that dispatcher instead of letting Generic Host own it.
        ApplicationHostService applicationHost =
            Services.GetRequiredService<ApplicationHostService>();
        await applicationHost.ShowAsync();
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        await _host.StopAsync();

        _host.Dispose();
    }

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
    }
}
