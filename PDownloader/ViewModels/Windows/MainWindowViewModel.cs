namespace PDownloader.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private bool _isInitialized = false;

        private readonly INavigationService _navigationService;

        public void OnNavigatedTo()
        {
            if (!_isInitialized)
                InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            _isInitialized = true;
        }

        [ObservableProperty]
        private string _applicationTitle = "PDownloader";

        [ObservableProperty]
        private ObservableCollection<object> _menuItems;

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems;

        public MainWindowViewModel(INavigationService navigationService, UpdateHostService updateHostService)
        {
            NavigationHandle.NavigationService = navigationService;
            _navigationService = navigationService;
            _menuItems = NavigationHandle.GetNavCardsInNamespace("PDownloader.Views.Pages");
            _footerMenuItems = NavigationHandle.GetNavCardsInNamespace("PDownloader.Views.PagesBottom");

            LanguageBase.LanguageChanged += (lang) =>
            {
                ConfluxManager.cfsPDownloaderCore?.Send("main-event", "OnLanguageChanged");
            };

            _ = updateHostService.CheckAsync(release =>
            {
                MessengerService.ShowSnackbar("sys_notification_title", LanguageBase.GetLangValue("update_available_summary", release.TagName), ControlAppearance.Caution, new SymbolIcon(SymbolRegular.ArrowDownload24), TimeSpan.FromSeconds(15));
            });
        }
    }
}
