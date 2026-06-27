namespace PDownloader.Views.Pages
{
    [PageMeta("page_downloads_title", "page_downloads_summary", SymbolRegular.ArrowTrendingSettings24, 1, false)]
    public partial class DownloadsPage : Page
    {
        public DownloadsViewModel ViewModel { get; }

        public DownloadsPage(DownloadsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
