using PDownloader.Services.DownloadServices;

namespace PDownloader.ViewModels.Pages
{
    public partial class ConfigViewModel : ObservableObject
    {
        private readonly AppSettings _settings;
        private readonly DownloadLauncherService _launcher;

        [ObservableProperty] 
        private string _defaultDownloadFolder  = string.Empty;

        [ObservableProperty]
        private int _defaultThreadCount = 8;

        [ObservableProperty]
        private int _maxConcurrentDownloads = 3;

        [ObservableProperty]
        private bool _isDaemonRunning;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ConfigViewModel(DownloadLauncherService launcher)
        {
            _settings = SharedMem.AppSettings ?? new AppSettings();
            _launcher = launcher;
            LoadFromSettings();
            IsDaemonRunning = _launcher.IsDaemonRunning;
            StatusMessage = IsDaemonRunning
                ? LanguageBase.GetLangValue("page_config_svc_active_title")
                : LanguageBase.GetLangValue("page_config_svc_inactive_title");
        }

        private void LoadFromSettings()
        {
            DefaultDownloadFolder  = _settings.DefaultDownloadFolder;
            DefaultThreadCount     = _settings.DefaultThreadCount;
            MaxConcurrentDownloads = _settings.MaxConcurrentDownloads;
        }

        [RelayCommand]
        private void BrowseDownloadFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = LanguageBase.GetLangValue("page_config_folder_title"),
                InitialDirectory = DefaultDownloadFolder,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                DefaultDownloadFolder = dialog.FolderName;
            }
        }

        [RelayCommand]
        private void SaveSettings()
        {
            _settings.DefaultDownloadFolder  = DefaultDownloadFolder;
            _settings.DefaultThreadCount     = DefaultThreadCount;
            _settings.MaxConcurrentDownloads = MaxConcurrentDownloads;
            _settings.Save();

            _launcher.ApplySettings(_settings);
            StatusMessage = LanguageBase.GetLangValue("page_config_save") + " ✓";
        }

        [RelayCommand]
        private void OpenRunner()
        {
            _launcher.ShowRunner();
            StatusMessage = LanguageBase.GetLangValue("page_config_open_runner") + "...";
        }
    }
}
