namespace PDownloader.Runner.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject, Services.INavigationAware
    {
        private bool _isInitialized = false;

        private Services.INavigationService _navigationService { get; set; }

        private RunnerConfig _runnerConfig { get; set; }

        [ObservableProperty]
        private string _applicationTitle = "PDownloader";

        public MainWindowViewModel(Services.INavigationService navigationService, RunnerConfig runnerConfig)
        {
            _navigationService = navigationService;
            _runnerConfig = runnerConfig;

            if (!_isInitialized)
                InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            _isInitialized = true;
        }

        public Task OnNavigatedToAsync()
        {
            if (_runnerConfig.IsArgsSetup)
            {
                if (_runnerConfig.IsRunner)
                {
                    _navigationService.NavigateTo(typeof(DownloaderProgressPage));
                }
                else
                {
                    _navigationService.NavigateTo(typeof(DownloaderPage));
                }
            }
            
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            return Task.CompletedTask;
        }
    }
}
