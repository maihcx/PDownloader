namespace PDownloader.ViewModels.Pages
{
    public partial class HomeViewModel : ObservableObject
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private ICollection<NavigationCard> _navigationCards = NavigationHandle.GetNavigationCards(["PDownloader.Views.Pages", "PDownloader.Views.PagesBottom"], typeof(HomePage));

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            _isInitialized = true;
        }

        [ObservableProperty]
        private string _appName = AppInfoHelper.AppName;

        [ObservableProperty]
        private string _author = AppInfoHelper.Author;

        [ObservableProperty]
        private string _sortAuthor = AppInfoHelper.SortAuthor;

        [ObservableProperty]
        private string _authorCreated = AppInfoHelper.AuthorCreated;

        [ObservableProperty]
        private string _appDescription = AppInfoHelper.AppDescription;
    }
}
