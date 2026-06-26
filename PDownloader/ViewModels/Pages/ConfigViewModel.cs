using PDownloader.Models;
using PDownloader.Services;

namespace PDownloader.ViewModels.Pages
{
    public partial class ConfigViewModel : ObservableObject
    {
        private readonly AppSettings _settings;
        private readonly DownloadLauncherService _launcher;

        [ObservableProperty] private string _defaultDownloadFolder  = string.Empty;
        [ObservableProperty] private int    _defaultThreadCount      = 8;
        [ObservableProperty] private int    _maxConcurrentDownloads  = 3;
        [ObservableProperty] private bool   _isDaemonRunning;
        [ObservableProperty] private string _statusMessage           = string.Empty;

        public ConfigViewModel()
        {
            _settings = SharedMem.AppSettings ?? new AppSettings();
            _launcher = SharedMem.Launcher    ?? new DownloadLauncherService();
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
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description         = LanguageBase.GetLangValue("page_config_folder_title"),
                SelectedPath        = DefaultDownloadFolder,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                DefaultDownloadFolder = dlg.SelectedPath;
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
