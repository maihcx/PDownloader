namespace PDownloader.Runner.Views
{
    /// <summary>
    /// Interaction logic for DownloaderPage.xaml
    /// </summary>
    public partial class DownloaderProgressPage : INavigableView<DownloaderProgressViewModel>
    {
        public DownloaderProgressViewModel ViewModel { get; }

        public DownloaderProgressPage(DownloaderProgressViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
