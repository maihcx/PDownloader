namespace PDownloader.ViewModels.Pages
{
    public partial class ConfigViewModel : ObservableObject
    {
        private readonly DownloadConfigService _configService;
        private readonly DownloadLauncherService _launcher;

        [ObservableProperty] 
        private DownloadConfigs? _downloadConfigs;

        [ObservableProperty]
        private bool _isDaemonRunning;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ConfigViewModel(DownloadLauncherService launcher, DownloadConfigService configService)
        {
            _configService = configService;
            _downloadConfigs = configService.DownloadConfigs;
            _launcher = launcher;

            IsDaemonRunning = _launcher.IsDaemonRunning;
            StatusMessage = IsDaemonRunning
                ? LanguageBase.GetLangValue("page_config_svc_active_title")
                : LanguageBase.GetLangValue("page_config_svc_inactive_title");
        }

        [RelayCommand]
        private void BrowseDownloadFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = LanguageBase.GetLangValue("page_config_folder_title"),
                InitialDirectory = DownloadConfigs?.DefaultDownloadFolder,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                DownloadConfigs?.DefaultDownloadFolder = dialog.FolderName;
            }
        }

        [RelayCommand]
        private void SaveSettings()
        {
            _configService.Save();

            _launcher.RefreshConfigs();
            StatusMessage = LanguageBase.GetLangValue("page_config_save") + " ✓";
        }
    }
}
