using PDownloader.Services;
using static PDownloader.Resources.ThemeConfigs;

namespace PDownloader.ViewModels.PagesBottom
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        public event Action? ScrollToUpdateRequested;

        private static ApplicationThemeManagerService? ThemeManagerService = WindowHelper.ThemeManagerService;

        private readonly UpdateHostService updateHostService;

        private readonly NavigationPanelHostService navigationPanelHostService;

        [ObservableProperty] private string _appVersion = string.Empty;

        public SettingsViewModel(
            UpdateHostService updateHostService,
            NavigationPanelHostService navigationPanelHostService
        )
        {
            this.updateHostService = updateHostService;
            this.navigationPanelHostService = navigationPanelHostService;

            _autoHideNavigationPanel = navigationPanelHostService.NaviPanelOpen == NaviPanelOpenState.Auto;
        }

        #region Update handling
        [ObservableProperty]
        private UpdateStatus _updateStatus = UpdateStatus.Idle;

        [ObservableProperty]
        private string _updateStatusText = string.Empty;

        [ObservableProperty]
        private string _latestVersion = string.Empty;

        [ObservableProperty]
        private string _releaseNotes = string.Empty;

        [ObservableProperty]
        private double _downloadProgress = 0;

        [ObservableProperty]
        private bool _isUpdateAvailable = false;

        [ObservableProperty]
        private bool _isReadyToInstall = false;

        [ObservableProperty]
        private bool _isDownloadReady = false;

        [ObservableProperty]
        private bool _isChecking = false;

        [ObservableProperty]
        private bool _isDownloading = false;

        partial void OnUpdateStatusChanged(UpdateStatus value)
        {
            IsChecking        = value == UpdateStatus.Checking;
            IsDownloading     = value == UpdateStatus.Downloading;
            IsReadyToInstall  = value == UpdateStatus.ReadyToInstall;
            IsDownloadReady   = value == UpdateStatus.UpdateAvailable;
            IsUpdateAvailable = value is UpdateStatus.UpdateAvailable
                                      or UpdateStatus.Downloading
                                      or UpdateStatus.ReadyToInstall;
        }

        private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(UpdateHostService.Status):
                        SyncStatusFromHost();
                        break;

                    case nameof(UpdateHostService.DownloadProgress):
                        DownloadProgress = updateHostService.DownloadProgress * 100;
                        UpdateStatusText = string.Format(
                            LanguageBase.GetLangValue("page_settings_update_downloading_progress"),
                            (int)(updateHostService.DownloadProgress * 100));
                        break;

                    case nameof(UpdateHostService.LatestRelease):
                        if (updateHostService.LatestRelease is { } release)
                        {
                            LatestVersion = release.TagName;
                            ReleaseNotes  = release.Body;
                        }
                        break;
                }
            });
        }

        private void SyncStatusFromHost()
        {
            UpdateStatus = updateHostService.Status;

            UpdateStatusText = updateHostService.Status switch
            {
                UpdateStatus.Idle => LanguageBase.GetLangValue("page_settings_update_idle"),
                UpdateStatus.Checking => LanguageBase.GetLangValue("page_settings_update_checking"),
                UpdateStatus.UpToDate => LanguageBase.GetLangValue("page_settings_update_uptodate"),
                UpdateStatus.Downloading => LanguageBase.GetLangValue("page_settings_update_downloading"),
                UpdateStatus.ReadyToInstall => LanguageBase.GetLangValue("page_settings_update_ready"),
                UpdateStatus.UpdateAvailable => string.Format(LanguageBase.GetLangValue("page_settings_update_available", updateHostService.LatestRelease?.TagName ?? "0.0.0")),
                UpdateStatus.Error => updateHostService.ErrorMessage
                    ?? LanguageBase.GetLangValue("page_settings_update_error"),
                _ => string.Empty
            };
        }
        #endregion

        #region Navigation panel auto hide
        [ObservableProperty]
        private bool _autoHideNavigationPanel;

        partial void OnAutoHideNavigationPanelChanged(bool value)
        {
            if (value)
            {
                navigationPanelHostService.NaviPanelOpen = NaviPanelOpenState.Auto;
            }
            else
            {
                navigationPanelHostService.NaviPanelOpen = 
                    navigationPanelHostService.GetIsPanelInternalOpen() ? 
                    NaviPanelOpenState.Open : NaviPanelOpenState.Close;
            }
        }
        #endregion

        #region Language list handle
        [ObservableProperty]
        private LanguageItem _selectedLanguage = LanguageBase.GetCurrentLanguageItem();

        [ObservableProperty]
        private ObservableCollection<LanguageItem> _languages = LanguageBase.GetLanguageItems();

        partial void OnSelectedLanguageChanged(LanguageItem value)
        {
            LanguageBase.SetLanguage(value.Code ?? "en");
        }
        #endregion

        #region Theme list handle
        [ObservableProperty]
        private Models.ComboBoxItem? _selectedTheme = ThemeManagerService?.GetThemeCBBSelected();

        [ObservableProperty]
        private ObservableCollection<Models.ComboBoxItem>? _themeList = ThemeManagerService?.GetThemeCBBs();

        partial void OnSelectedThemeChanged(Models.ComboBoxItem? value)
        {
            ThemeManagerService?.SetApplicationTheme(Enum.Parse<IThemeType>(value?.Value ?? "Mica"));
            ConfluxManager.cfsPDownloaderCore?.Send("main-event", "OnThemeChanged");
        }
        #endregion

        #region Material list handle
        [ObservableProperty]
        private Models.ComboBoxItem? _selectedMaterial = ThemeManagerService?.GetMaterialCBBSelected();

        [ObservableProperty]
        private ObservableCollection<Models.ComboBoxItem>? _materialList = ThemeManagerService?.GetMaterialCBBs();

        partial void OnSelectedMaterialChanged(Models.ComboBoxItem? value)
        {
            ThemeManagerService?.SetBackdropType(Enum.Parse<WindowBackdropType>(value?.Value ?? "Mica"));
            ThemeManagerService?.SetApplicationTheme(Enum.Parse<IThemeType>(SelectedTheme?.Value ?? "Auto"));
            ConfluxManager.cfsPDownloaderCore?.Send("main-event", "OnMaterialChanged");
        }
        #endregion

        #region CornerRadius list handle
        [ObservableProperty]
        private int _sliderCornerRadius = ThemeManagerService?.GlobalCornerRadius ?? 0;

        partial void OnSliderCornerRadiusChanged(int oldValue, int newValue)
        {
            ThemeManagerService?.GlobalCornerRadius = newValue;
            ConfluxManager.cfsPDownloaderCore?.Send("main-event", "OnRadiusChanged");
        }
        #endregion

        #region StartWithWin
        [ObservableProperty]
        private bool _isStartWithWin = UserDataStore.GetValue<bool>("IsStartAtBoot");

        partial void OnIsStartWithWinChanged(bool oldValue, bool newValue)
        {
            StartupManager.SetStartWithWin(newValue);
        }
        #endregion

        #region IsViewAtBoot
        [ObservableProperty]
        private bool _isViewAtBoot = UserDataStore.GetValue<bool>("IsViewAtBoot");

        partial void OnIsViewAtBootChanged(bool oldValue, bool newValue)
        {
            UserDataStore.SetValue("IsViewAtBoot", newValue);
        }
        #endregion

        [ObservableProperty]
        private string _copyRight = AppInfoHelper.CopyRight;

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            ScrollToUpdateRequested?.Invoke();

            updateHostService.PropertyChanged += OnHostPropertyChanged;

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            updateHostService.PropertyChanged -= OnHostPropertyChanged;

            return Task.CompletedTask;
        }

        private void InitializeViewModel()
        {
            var v = UpdateService.GetCurrentVersion();
            AppVersion = $"PDownloader - {v.Major}.{v.Minor}.{v.Build}";
            UpdateStatusText = LanguageBase.GetLangValue("page_settings_update_idle");
            _isInitialized = true;

            _ = CheckForUpdateAsync();

            LanguageBase.LanguageChanged += (string lang) =>
            {
                _ = CheckForUpdateAsync();
            };
        }


        [RelayCommand]
        private Task CheckForUpdateAsync()
        {
            LatestVersion    = string.Empty;
            ReleaseNotes     = string.Empty;
            DownloadProgress = 0;
            return updateHostService.CheckAsync();
        }

        [RelayCommand]
        private Task DownloadAndInstallAsync()
        {
            return updateHostService.DownloadAsync();
        }

        [RelayCommand]
        private void InstallUpdate()
        {
            try { updateHostService.LaunchInstaller(); }
            catch (Exception ex)
            {
                UpdateStatus     = UpdateStatus.Error;
                UpdateStatusText = ex.Message;
            }
        }

        [RelayCommand]
        private void CancelUpdate()
        {
            updateHostService.Cancel();
            DownloadProgress = 0;
        }
    }
}
