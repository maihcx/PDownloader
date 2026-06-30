using System.Windows.Navigation;

namespace PDownloader.Runner.ViewModels.Windows
{
    public partial class DownloaderViewModel : ObservableObject
    {
        private bool _isInitialized = false;

        private Services.INavigationService _navigationService { get; set; }

        private DownloaderService _downloaderService;

        [ObservableProperty]
        private RunnerConfig _runnerConfig;

        [ObservableProperty]
        private DownloaderServiceStatus _downloaderStatus;

        public DownloaderViewModel(Services.INavigationService navigationService, RunnerConfig runnerConfig, DownloaderService downloaderService)
        {
            _navigationService = navigationService;
            RunnerConfig = runnerConfig;
            _downloaderService = downloaderService;
            _downloaderStatus = downloaderService.DownloaderStatus;

            if (!_isInitialized)
                InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            _isInitialized = true;
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = LanguageBase.GetLangValue("select_folder_title"),
                InitialDirectory = RunnerConfig.SaveTo
            };
            if (dlg.ShowDialog() == true)
            {
                RunnerConfig.SaveTo = dlg.FolderName;
            }
        }

        [RelayCommand]
        private void CancelDownload()
        {
            Application.Current.Shutdown();
        }

        [RelayCommand]
        private async Task ConfirmDownload()
        {
            var status = await _downloaderService.StartDownload();

            if (status.State == RunnerState.Downloading)
            {
                _navigationService.NavigateTo(typeof(DownloaderProgressPage));
            }
        }
    }
}
