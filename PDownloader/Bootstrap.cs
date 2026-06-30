namespace PDownloader
{
    public static class Bootstrap
    {
        private static Thread?   _pipeThread;
        private static Mutex?    _mutex;
        private static string    UniqueAppId = @"Global\PDownloader.SingleInstance.App";
        private static bool      _isPrimaryInstance = false;
        private static SplashScreen? SplashScreen;

        public static bool IsViewAtBoot { get; set; }
        public static bool IsEndService  { get; set; }

        public static void OnBeforeStartup()
        {
            SharedMem.AppSettings = AppSettings.Load();
            SharedMem.Launcher    = new DownloadLauncherService();

            #region Mutex checker
            try
            {
                _mutex = CreateMutexWithSecurity(UniqueAppId);
                _isPrimaryInstance = _mutex.WaitOne(0, false);
            }
            catch
            {
                _mutex = new Mutex(true, UniqueAppId, out _isPrimaryInstance);
            }

            if (!_isPrimaryInstance)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", UniqueAppId, PipeDirection.Out);
                    client.Connect(1000);
                    using var writer = new StreamWriter(client) { AutoFlush = true };
                    writer.WriteLine("SHOW");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to connect to pipe: {ex.Message}");
                }
                Environment.Exit(0);
                return;
            }
            #endregion

            #region SplashScreen
            SplashScreen = new SplashScreen("Assets/app-256.png");
            SplashScreen.Show(false, true);
            #endregion

            #region Core CFS init
            ConfluxService cfsPDownloaderCore = new();
            cfsPDownloaderCore.CreateNoWindow = true;
            cfsPDownloaderCore.Register(
                "PDownloader Core.exe",
                "PDownloader.MainToCore",
                "PDownloader.CoreToMain");

            IsViewAtBoot = cfsPDownloaderCore.IsAppStarted();

            if (UserDataStore.GetValue<bool>("IsViewAtBoot"))
                IsViewAtBoot = true;

            cfsPDownloaderCore.StartApp();
            _ = cfsPDownloaderCore.StartServiceAsync();

            ConfluxManager.cfsPDownloaderCore = cfsPDownloaderCore;

            cfsPDownloaderCore.OnMessageReceived += DownloadsChannel.Handle;
            cfsPDownloaderCore.OnMessageReceived += (name, value) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (name == "state")
                    {
                        switch (value)
                        {
                            case "start":
                                WindowHelper.FocusMainWindow();
                                break;
                            case "shutdown":
                                IsEndService = true;
                                System.Windows.Application.Current.Shutdown();
                                break;
                        }
                    }
                    else if (name == "tray-event")
                    {
                        WindowHelper.FocusMainWindow();
                        switch (value)
                        {
                            case "OnGoHome":
                                NavigationHandle.NavigationService?.Navigate(typeof(HomePage));
                                break;
                            case "OnGoConfig":
                                NavigationHandle.NavigationService?.Navigate(typeof(ConfigPage));
                                break;
                            case "OnGoDownload":
                                NavigationHandle.NavigationService?.Navigate(typeof(DownloadsPage));
                                break;
                            case "OnGoSettings":
                            case "OnGoSettings--UPDATE":
                                if (value == "OnGoSettings--UPDATE")
                                    SharedMem.IsScrollToUpdateCard = true;
                                NavigationHandle.NavigationService?.Navigate(typeof(SettingsPage));
                                break;
                            case "OnShowRunner":
                                SharedMem.Launcher?.ShowRunner();
                                break;
                        }
                    }
                });
            };
            #endregion

            #region Single-instance pipe server
            _pipeThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        var pipeSecurity = new PipeSecurity();
                        pipeSecurity.AddAccessRule(new PipeAccessRule(
                            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                            PipeAccessRights.ReadWrite,
                            AccessControlType.Allow));

                        using var server = NamedPipeServerStreamAcl.Create(
                            UniqueAppId, PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.None,
                            0, 0, pipeSecurity);

                        server.WaitForConnection();
                        using var reader = new StreamReader(server);
                        string? line = reader.ReadLine();
                        if (line == "SHOW")
                            App.Current.Dispatcher.Invoke(WindowHelper.FocusMainWindow);
                    }
                    catch { Thread.Sleep(100); }
                }
            });
            _pipeThread.IsBackground = true;
            _pipeThread.Start();
            #endregion
        }

        public static void OnStartup()
        {
            StartupManager.RefreshStartWithWin();
            SplashScreen?.Close(new TimeSpan(0));

            if (!IsViewAtBoot)
                App.Current.Shutdown();
        }

        public static void OnExit()
        {
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
        }

        private static Mutex CreateMutexWithSecurity(string name)
        {
            var rule = new MutexAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow);
            var sec = new MutexSecurity();
            sec.AddAccessRule(rule);
            var m = new Mutex(false, name, out bool created);
            if (created) m.SetAccessControl(sec);
            return m;
        }
    }
}
