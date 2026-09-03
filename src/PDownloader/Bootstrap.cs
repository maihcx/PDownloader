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

public static class Bootstrap
{
    private static Thread? _pipeThread;
    private static Mutex? _mutex;
    private static readonly string _uniqueAppId = @"Global\PDownloader.SingleInstance.App-" + IpcUserScope.CurrentUserId;
    private static readonly string _activationPipe = "PDownloader.MainActivation-" + IpcUserScope.CurrentUserId;
    private static bool _isPrimaryInstance = false;
    private static SplashScreen? SplashScreen;

    public static bool IsViewAtBoot { get; set; }
    public static bool IsEndService { get; set; }

    public static void OnBeforeStartup()
    {
        #region Mutex checker
        _mutex = CreateMutexWithSecurity(_uniqueAppId);
        try
        {
            _isPrimaryInstance = _mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            _isPrimaryInstance = true;
        }

        if (!_isPrimaryInstance)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _activationPipe, PipeDirection.Out);
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
            IpcTopology.CoreProcessName,
            IpcTopology.MainToCorePipeName,
            IpcTopology.CoreToMainPipeName);

        IsViewAtBoot = cfsPDownloaderCore.IsReadyAsync().GetAwaiter().GetResult();
        cfsPDownloaderCore.StartAndWaitUntilReadyAsync().GetAwaiter().GetResult();
        cfsPDownloaderCore.SetReady(false);
        UserDataStore.InitializeAsync().GetAwaiter().GetResult();

        if (UserDataStore.GetValue<bool>("IsViewAtBoot"))
        {
            IsViewAtBoot = true;
        }

        ConfluxManager.cfsPDownloaderCore = cfsPDownloaderCore;

        cfsPDownloaderCore.OnMessageReceived += App.GetRequiredService<DownloadsChannelService>().Handle;
        cfsPDownloaderCore.OnMessageReceived += App.GetRequiredService<UpdateHostService>().Handle;
        cfsPDownloaderCore.OnMessageReceived += message =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (message.TryGetPayload(AppProtocol.State, out AppState state))
                {
                    switch (state)
                    {
                        case AppState.Start:
                            WindowHelper.FocusMainWindow();
                            break;
                        case AppState.Shutdown:
                            IsEndService = true;
                            System.Windows.Application.Current.Shutdown();
                            break;
                    }
                }
                else if (message.TryGetPayload(
                             AppProtocol.TrayEvent,
                             out TrayNavigationEvent trayEvent))
                {
                    WindowHelper.FocusMainWindow();
                    switch (trayEvent)
                    {
                        case TrayNavigationEvent.GoHome:
                            NavigationHandle.NavigationService?.Navigate(typeof(HomePage));
                            break;
                        case TrayNavigationEvent.GoConfig:
                            NavigationHandle.NavigationService?.Navigate(typeof(ConfigPage));
                            break;
                        case TrayNavigationEvent.GoDownload:
                            NavigationHandle.NavigationService?.Navigate(typeof(DownloadsPage));
                            break;
                        case TrayNavigationEvent.GoSettings:
                        case TrayNavigationEvent.GoSettingsUpdate:
                            if (trayEvent == TrayNavigationEvent.GoSettingsUpdate)
                            {
                                SharedMem.IsScrollToUpdateCard = true;
                            }

                            NavigationHandle.NavigationService?.Navigate(typeof(SettingsPage));
                            break;
                        case TrayNavigationEvent.GoAbout:
                            NavigationHandle.NavigationService?.Navigate(typeof(AboutPage));
                            break;
                    }
                }
            });
        };
        _ = cfsPDownloaderCore.StartServiceAsync();
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
                        new SecurityIdentifier(IpcUserScope.CurrentUserId),
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));

                    using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
                        _activationPipe, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None,
                        0, 0, pipeSecurity);

                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string? line = reader.ReadLine();
                    if (line == "SHOW")
                    {
                        App.Current.Dispatcher.Invoke(WindowHelper.FocusMainWindow);
                    }
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

        ConfluxManager.cfsPDownloaderCore?.SetReady(true);
        _ = ConfluxManager.cfsPDownloaderCore?.SendAsync(
            AppProtocol.MainReady);
        _ = App.GetRequiredService<UpdateHostService>().RequestStateAsync();

        if (!IsViewAtBoot)
        {
            App.Current.Shutdown();
        }
    }

    public static void OnExit()
    {
        ConfluxManager.cfsPDownloaderCore?.Dispose();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }

            _mutex.Dispose();
        }
    }

    private static Mutex CreateMutexWithSecurity(string name)
    {
        var rule = new MutexAccessRule(
            new SecurityIdentifier(IpcUserScope.CurrentUserId),
            MutexRights.FullControl,
            AccessControlType.Allow);
        var sec = new MutexSecurity();
        sec.AddAccessRule(rule);
        var m = new Mutex(false, name, out bool created);
        if (created)
        {
            m.SetAccessControl(sec);
        }

        return m;
    }
}
