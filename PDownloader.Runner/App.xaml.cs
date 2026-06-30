namespace PDownloader.Runner
{
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
                    services.AddHostedService<ApplicationHostService>();
                    services.AddSingleton<DownloaderService>();

                    services.AddHostedService(sp =>
                        sp.GetRequiredService<DownloaderService>());

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
            await _host.StartAsync();

            TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();
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
}
