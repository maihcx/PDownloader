namespace PDownloader.Views.Pages
{
    [PageMeta("page_home_title", "page_home_summary", SymbolRegular.Home48, 0, false)]
    public partial class HomePage : INavigableView<HomeViewModel>
    {
        public HomeViewModel ViewModel { get; }

        public HomePage(HomeViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
