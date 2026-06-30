namespace PDownloader.Runner;

public class Bootstrap
{
    private readonly App _app;
    private readonly string[] _args;
    private ConfluxService? _cfs;
    private RunnerWindow? _mainWindow;

    public Bootstrap(string[] args, App app)
    {
        _args = args;
        _app = app;
    }

    public void OnStarted()
    {
        // Parse args: --cfx-mode <json payload> or --download <url> --save-to <path>
        var config = RunnerConfig.ParseArgs(_args);
        AppRuntime.Config = config;

        TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();

        // Connect back to Core via CFS to receive download commands
        _cfs = new ConfluxService();
        _cfs.Register(
            "PDownloader Core.exe",
            $"PDownloader.RunnerToCore-{config.Token}",   // send pipe
            $"PDownloader.CoreToRunner-{config.Token}"    // receive pipe
        );
        AppRuntime.Cfs = _cfs;
        _cfs.OnMessageReceiving += RunnerCommandHandler.Handle;
        _cfs.OnMessageReceived += DownloadsChannel.Handle;
        _ = _cfs.StartServiceAsync();

        // If a URL was passed directly (e.g. from extension), open download dialog immediately
        _app.ShutdownMode = ShutdownMode.OnLastWindowClose;

        _mainWindow = new RunnerWindow();
        AppRuntime.MainWindow = _mainWindow;
        _mainWindow.Show();
        _mainWindow.Activate();

        if (!string.IsNullOrWhiteSpace(config.InitialUrl))
        {
            _mainWindow.ShowForDownload(config.InitialUrl, config.SaveTo, config.FileName);
        }
    }

    public void OnStopped()
    {
        _ = _cfs?.StopServiceAsync();
    }
}