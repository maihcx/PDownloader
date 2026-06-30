using System.Diagnostics;

namespace PDownloader.Tray.ViewModels
{
    public partial class MainWindowViewModels : ObservableObject
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private string _applicationTitle = "PDownloader";

        [ObservableProperty]
        private ObservableCollection<MenuItem>? _trayMenuItems;

        private ConfluxService? CoreService;

        private string? _updateUrl;

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
            CoreService.Register("PDownloader Core.exe", "PDownloader.TrayToCore", "PDownloader.CoreToTray");

            CoreService.OnMessageReceived += (name, value) =>
            {
                App.Current.Dispatcher.Invoke(() => {
                    if (name == "main-event")
                    {
                        switch (value)
                        {
                            case "OnLanguageChanged":
                                UserDataStore.Reload();
                                TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();
                                createTrayIcons(hasUpdate: _updateUrl != null);
                                _ = CheckUpdateAfterDelayAsync(TimeSpan.FromSeconds(0));
                                break;

                            case "OnRadiusChanged":
                                UserDataStore.Reload();
                                Application.Current.Resources["ControlCornerRadius"] = new CornerRadius(UserDataStore.GetValue<int>("ObjectCornerRadius"));
                                break;

                            case "OnMaterialChanged":
                                UserDataStore.Reload();
                                AppRuntime.ThemeManagerService?.SetBackdropType(Enum.Parse<WindowBackdropType>(AppRuntime.ThemeManagerService.GetMaterialCBBSelected()?.Value ?? "Mica"));
                                AppRuntime.ThemeManagerService?.SetApplicationTheme(Enum.Parse<ThemeConfigs.IThemeType>(AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                                break;

                            case "OnThemeChanged":
                                UserDataStore.Reload();
                                AppRuntime.ThemeManagerService?.SetApplicationTheme(Enum.Parse<ThemeConfigs.IThemeType>(AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                                break;

                            case "OnAppExit":
                                Application.Current.Shutdown();
                                break;
                        }
                    }
                });
            };

            _ = CoreService.StartServiceAsync();
            AppRuntime.CoreService = CoreService;

            _ = CheckUpdateAfterDelayAsync(TimeSpan.FromSeconds(5));
        }

        private async Task CheckUpdateAfterDelayAsync(TimeSpan delay)
        {
            await Task.Delay(delay);

            var info = await UpdateChecker.CheckAsync();
            if (info is null) return;

            _updateUrl = info.HtmlUrl;

            App.Current.Dispatcher.Invoke(() =>
            {
                createTrayIcons(hasUpdate: true, updateVersion: info.TagName);

                ShowUpdateBalloon(info.TagName);
            });
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
                    CoreService?.Send("state", "start");
                    break;
                case "tray_home":
                    CoreService?.StartApp();
                    CoreService?.Send("tray-event", "OnGoHome");
                    break;
                case "tray_config":
                    CoreService?.StartApp();
                    CoreService?.Send("tray-event", "OnGoConfig");
                    break;
                case "tray_download":
                    CoreService?.StartApp();
                    CoreService?.Send("tray-event", "OnGoDownload");
                    break;
                case "tray_settings":
                    CoreService?.StartApp();
                    CoreService?.Send("tray-event", "OnGoSettings");
                    break;
                case "tray_update":
                    CoreService?.StartApp();
                    CoreService?.Send("tray-event", "OnGoSettings--UPDATE");
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
                    Icon    = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
                    Header  = label,
                    Tag     = "tray_update",
                    Command = TrayExecuteCommand,
                    CommandParameter = "tray_update",
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 150, 255)),
                });

                //items.Add(new Separator());
            }

            items.Add(new MenuItem
            {
                Icon    = new SymbolIcon { Symbol = SymbolRegular.Open24 },
                Header  = LocalizationHelper.GetLang("open_title"), Tag = "tray_open",
                Command = TrayExecuteCommand, CommandParameter = "tray_open"
            });
            items.Add(new MenuItem
            {
                Icon    = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                Header  = LocalizationHelper.GetLang("page_home_title"), Tag = "tray_home",
                Command = TrayExecuteCommand, CommandParameter = "tray_home"
            });
            items.Add(new MenuItem
            {
                Icon    = new SymbolIcon { Symbol = SymbolRegular.ArrowTrendingSettings24 },
                Header  = LocalizationHelper.GetLang("page_config_title"), Tag = "tray_config",
                Command = TrayExecuteCommand, CommandParameter = "tray_config"
            });
            items.Add(new MenuItem
            {
                Icon    = new SymbolIcon { Symbol = SymbolRegular.DrawerArrowDownload24 },
                Header  = LocalizationHelper.GetLang("page_download_title"), Tag = "tray_download",
                Command = TrayExecuteCommand, CommandParameter = "tray_download"
            });
            items.Add(new MenuItem
            {
                Icon    = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                Header  = LocalizationHelper.GetLang("page_settings_title"), Tag = "tray_settings",
                Command = TrayExecuteCommand, CommandParameter = "tray_settings"
            });
            items.Add(new MenuItem
            {
                Icon    = new SymbolIcon { Symbol = SymbolRegular.ArrowExit20 },
                Header  = LocalizationHelper.GetLang("exit_title"), Tag = "tray_close",
                Command = TrayExecuteCommand, CommandParameter = "tray_close"
            });

            TrayMenuItems = items;
        }
    }
}