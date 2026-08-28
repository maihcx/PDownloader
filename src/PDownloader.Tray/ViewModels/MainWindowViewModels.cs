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

namespace PDownloader.Tray.ViewModels;

public partial class MainWindowViewModels : ObservableObject, IDisposable
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private string _applicationTitle = "PDownloader";

    [ObservableProperty]
    private ObservableCollection<MenuItem>? _trayMenuItems;

    private ConfluxService? CoreService;

    private string? _updateVersion;
    private string? _lastNotifiedUpdateVersion;

    private bool _disposed;

    public MainWindowViewModels()
    {
        if (!_isInitialized)
        {
            InitializeViewModel();
        }
    }

    private void InitializeViewModel()
    {
        _isInitialized = true;

        createTrayIcons();

        CoreService = new ConfluxService();
        CoreService.CreateNoWindow = true;
        CoreService.Register(
            IpcTopology.CoreProcessName,
            IpcTopology.TrayToCorePipeName,
            IpcTopology.CoreToTrayPipeName);

        CoreService.OnMessageReceived += message =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (message.TryGetPayload(UpdateProtocol.State, out UpdateStateSnapshot snapshot))
                {
                    ApplyUpdateState(snapshot);
                }
                else if (message.TryGetPayload(AppProtocol.MainEvent, out MainAppEvent mainEvent))
                {
                    switch (mainEvent)
                    {
                        case MainAppEvent.LanguageChanged:
                            UserDataStore.Reload();
                            TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();
                            createTrayIcons(
                                hasUpdate: _updateVersion != null,
                                updateVersion: _updateVersion);
                            break;

                        case MainAppEvent.RadiusChanged:
                            UserDataStore.Reload();
                            Application.Current.Resources["ControlCornerRadius"] = new CornerRadius(UserDataStore.GetValue<int>("ObjectCornerRadius"));
                            break;

                        case MainAppEvent.MaterialChanged:
                            UserDataStore.Reload();
                            AppRuntime.ThemeManagerService?.SetBackdropType(Enum.Parse<WindowBackdropType>(AppRuntime.ThemeManagerService.GetMaterialCBBSelected()?.Value ?? "Mica"));
                            AppRuntime.ThemeManagerService?.SetApplicationTheme(Enum.Parse<ThemeConfigs.IThemeType>(AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                            break;

                        case MainAppEvent.ThemeChanged:
                            UserDataStore.Reload();
                            AppRuntime.ThemeManagerService?.SetApplicationTheme(Enum.Parse<ThemeConfigs.IThemeType>(AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                            break;

                        case MainAppEvent.AppExit:
                            Application.Current.Shutdown();
                            break;
                    }
                }
            });
        };

        _ = CoreService.StartServiceAsync();
        AppRuntime.CoreService = CoreService;

        _ = RequestUpdateStateAsync();
        _ = RequestUpdateCheckAfterDelayAsync(TimeSpan.FromSeconds(5));
    }

    private async Task RequestUpdateStateAsync()
    {
        if (CoreService is not { } coreService)
        {
            return;
        }

        IpcRequestResult<UpdateStateSnapshot> result =
            await coreService.RequestAsync(UpdateProtocol.GetState);

        if (result.Success && result.Value is { } snapshot)
        {
            App.Current.Dispatcher.Invoke(() => ApplyUpdateState(snapshot));
        }
    }

    private async Task RequestUpdateCheckAfterDelayAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        if (CoreService is { } coreService)
        {
            await coreService.SendAsync(
                UpdateProtocol.Command,
                new UpdateCommandRequest(UpdateCommandKind.Check));
        }
    }

    private void ApplyUpdateState(UpdateStateSnapshot snapshot)
    {
        bool hasUpdate = snapshot.Status is UpdateStatus.UpdateAvailable
            or UpdateStatus.Downloading
            or UpdateStatus.ReadyToInstall;
        string? newVersion = hasUpdate
            ? snapshot.LatestRelease?.TagName
            : null;
        bool shouldNotify = snapshot.Status == UpdateStatus.UpdateAvailable
            && snapshot.ShouldNotifyTray
            && !string.IsNullOrWhiteSpace(newVersion)
            && !string.Equals(
                _lastNotifiedUpdateVersion,
                newVersion,
                StringComparison.OrdinalIgnoreCase);

        _updateVersion = newVersion;
        createTrayIcons(hasUpdate, newVersion);

        if (shouldNotify)
        {
            _lastNotifiedUpdateVersion = newVersion;
            ShowUpdateBalloon(newVersion!);
        }
    }

    private void ShowUpdateBalloon(string version)
    {
        if (AppRuntime.MainWindow is Views.MainWindow win)
        {
            win.ShowUpdateBalloon(version);
        }
    }

    [RelayCommand]
    public void OnTrayExecute(string? tag)
    {
        switch (tag)
        {
            case "tray_open":
                CoreService?.StartApp();
                _ = CoreService?.SendAsync(
                    AppProtocol.State,
                    AppState.Start);
                break;
            case "tray_home":
                CoreService?.StartApp();
                _ = CoreService?.SendAsync(
                    AppProtocol.TrayEvent,
                    TrayNavigationEvent.GoHome);
                break;
            case "tray_config":
                CoreService?.StartApp();
                _ = CoreService?.SendAsync(
                    AppProtocol.TrayEvent,
                    TrayNavigationEvent.GoConfig);
                break;
            case "tray_download":
                CoreService?.StartApp();
                _ = CoreService?.SendAsync(
                    AppProtocol.TrayEvent,
                    TrayNavigationEvent.GoDownload);
                break;
            case "tray_settings":
                CoreService?.StartApp();
                _ = CoreService?.SendAsync(
                    AppProtocol.TrayEvent,
                    TrayNavigationEvent.GoSettings);
                break;
            case "tray_update":
                CoreService?.StartApp();
                _ = CoreService?.SendAsync(
                    AppProtocol.TrayEvent,
                    TrayNavigationEvent.GoSettingsUpdate);
                break;
            case "tray_about":
                CoreService?.StartApp();
                _ = CoreService?.SendAsync(
                    AppProtocol.TrayEvent,
                    TrayNavigationEvent.GoAbout);
                break;
            case "tray_close":
                Application.Current.Shutdown();
                break;
        }
    }

    private void createTrayIcons(bool hasUpdate = false, string? updateVersion = null)
    {
        var items = new ObservableCollection<MenuItem>();

        if (hasUpdate)
        {
            string label = string.IsNullOrEmpty(updateVersion)
                ? LocalizationHelper.GetLang("update_available_title")
                : $"{LocalizationHelper.GetLang("update_available_title")} ({updateVersion})";

            items.Add(new MenuItem
            {
                Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
                Header = label,
                Tag = "tray_update",
                Command = TrayExecuteCommand,
                CommandParameter = "tray_update",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 150, 255)),
            });

            //items.Add(new Separator());
        }

        items.Add(new MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Open24 },
            Header = LocalizationHelper.GetLang("open_title"),
            Tag = "tray_open",
            Command = TrayExecuteCommand,
            CommandParameter = "tray_open"
        });
        items.Add(new MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
            Header = LocalizationHelper.GetLang("page_home_title"),
            Tag = "tray_home",
            Command = TrayExecuteCommand,
            CommandParameter = "tray_home"
        });
        items.Add(new MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.PersonSettings20 },
            Header = LocalizationHelper.GetLang("page_config_title"),
            Tag = "tray_config",
            Command = TrayExecuteCommand,
            CommandParameter = "tray_config"
        });
        items.Add(new MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.DrawerArrowDownload24 },
            Header = LocalizationHelper.GetLang("page_download_title"),
            Tag = "tray_download",
            Command = TrayExecuteCommand,
            CommandParameter = "tray_download"
        });
        items.Add(new MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
            Header = LocalizationHelper.GetLang("page_settings_title"),
            Tag = "tray_settings",
            Command = TrayExecuteCommand,
            CommandParameter = "tray_settings"
        });
        items.Add(new MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Info24 },
            Header = LocalizationHelper.GetLang("page_about_title"),
            Tag = "tray_about",
            Command = TrayExecuteCommand,
            CommandParameter = "tray_about"
        });
        items.Add(new MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowExit20 },
            Header = LocalizationHelper.GetLang("exit_title"),
            Tag = "tray_close",
            Command = TrayExecuteCommand,
            CommandParameter = "tray_close"
        });

        TrayMenuItems = items;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            CoreService?.Dispose();
            CoreService = null;
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
