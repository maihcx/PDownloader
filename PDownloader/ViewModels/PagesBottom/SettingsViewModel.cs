using PDownloader.Services;
using static PDownloader.Resources.ThemeConfigs;

namespace PDownloader.ViewModels.PagesBottom
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        public event Action? ScrollToUpdateRequested;

        private static ApplicationThemeManagerService? ThemeManagerService = WindowHelper.ThemeManagerService;

        private readonly UpdateService _updateService = new();
        private CancellationTokenSource? _updateCts;

        [ObservableProperty] private string _appVersion = string.Empty;

        // ── Update ─────────────────────────────────────────────────────────
        [ObservableProperty] private UpdateStatus _updateStatus = UpdateStatus.Idle;
        [ObservableProperty] private string _updateStatusText = string.Empty;
        [ObservableProperty] private string _latestVersion = string.Empty;
        [ObservableProperty] private string _releaseNotes = string.Empty;
        [ObservableProperty] private double _downloadProgress = 0;
        [ObservableProperty] private bool _isUpdateAvailable = false;
        [ObservableProperty] private bool _isReadyToInstall = false;
        [ObservableProperty] private bool _isDownloadReady = false;
        [ObservableProperty] private bool _isChecking = false;
        [ObservableProperty] private bool _isDownloading = false;

        partial void OnUpdateStatusChanged(UpdateStatus value)
        {
            IsChecking      = value == UpdateStatus.Checking;
            IsDownloading   = value == UpdateStatus.Downloading;
            IsReadyToInstall = value == UpdateStatus.ReadyToInstall;
            IsDownloadReady = value == UpdateStatus.UpdateAvailable;
            IsUpdateAvailable = value == UpdateStatus.UpdateAvailable || value == UpdateStatus.Downloading || value == UpdateStatus.ReadyToInstall;
        }

        #region Navigation panel auto hide
        [ObservableProperty]
        private bool _autoHideNavigationPanel = WindowHelper.IsAutoHideNavPanel;

        partial void OnAutoHideNavigationPanelChanged(bool oldValue, bool newValue)
        {
            WindowHelper.IsAutoHideNavPanel = AutoHideNavigationPanel = newValue;
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

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            _updateCts?.Cancel();
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
        private async Task CheckForUpdateAsync()
        {
            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var ct = _updateCts.Token;

            UpdateStatus     = UpdateStatus.Checking;
            UpdateStatusText = LanguageBase.GetLangValue("page_settings_update_checking");
            LatestVersion    = string.Empty;
            ReleaseNotes     = string.Empty;
            DownloadProgress = 0;

            try
            {
                bool hasUpdate = await _updateService.CheckForUpdateAsync(ct);

                if (ct.IsCancellationRequested) return;

                if (hasUpdate && _updateService.LatestRelease is { } release)
                {
                    LatestVersion    = release.TagName;
                    ReleaseNotes     = release.Body;
                    UpdateStatus     = UpdateStatus.UpdateAvailable;
                    UpdateStatusText = string.Format(
                        LanguageBase.GetLangValue("page_settings_update_available"),
                        release.TagName);
                }
                else
                {
                    UpdateStatus     = UpdateStatus.UpToDate;
                    UpdateStatusText = LanguageBase.GetLangValue("page_settings_update_uptodate");
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                UpdateStatus     = UpdateStatus.Error;
                UpdateStatusText = _updateService.ErrorMessage
                    ?? LanguageBase.GetLangValue("page_settings_update_error");
            }
        }

        [RelayCommand]
        private async Task DownloadAndInstallAsync()
        {
            if (!IsUpdateAvailable) return;

            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var ct = _updateCts.Token;

            UpdateStatus     = UpdateStatus.Downloading;
            UpdateStatusText = LanguageBase.GetLangValue("page_settings_update_downloading");
            DownloadProgress = 0;

            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p * 100;
                UpdateStatusText = string.Format(
                    LanguageBase.GetLangValue("page_settings_update_downloading_progress"),
                    (int)(p * 100));
            });

            try
            {
                await _updateService.DownloadInstallerAsync(progress, ct);

                if (ct.IsCancellationRequested) return;

                UpdateStatus     = UpdateStatus.ReadyToInstall;
                UpdateStatusText = LanguageBase.GetLangValue("page_settings_update_ready");
            }
            catch (OperationCanceledException) { }
            catch
            {
                UpdateStatus     = UpdateStatus.Error;
                UpdateStatusText = _updateService.ErrorMessage
                    ?? LanguageBase.GetLangValue("page_settings_update_error");
            }
        }

        [RelayCommand]
        private void InstallUpdate()
        {
            try
            {
                _updateService.LaunchInstaller();
            }
            catch (Exception ex)
            {
                UpdateStatus     = UpdateStatus.Error;
                UpdateStatusText = ex.Message;
            }
        }

        [RelayCommand]
        private void CancelUpdate()
        {
            _updateCts?.Cancel();
            UpdateStatus     = UpdateStatus.Idle;
            UpdateStatusText = LanguageBase.GetLangValue("page_settings_update_idle");
            DownloadProgress = 0;
        }
    }
}
