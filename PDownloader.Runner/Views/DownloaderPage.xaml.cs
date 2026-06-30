namespace PDownloader.Runner.Views
{
    /// <summary>
    /// Interaction logic for DownloaderPage.xaml
    /// </summary>
    public partial class DownloaderPage : INavigableView<DownloaderViewModel>
    {
        public DownloaderViewModel ViewModel { get; }

        public DownloaderPage(DownloaderViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
