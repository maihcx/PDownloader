namespace PDownloader.Runner.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : IWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public Frame FrameHost => this.FrameHostContent;

        public ApplicationThemeManagerService ThemeManagerService { get; }

        public MainWindow(MainWindowViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            ThemeManagerService = new ApplicationThemeManagerService(this);
            //WindowHelper.ThemeManagerService = ThemeManagerService;
            ThemeManagerService.InitCornerRadius();
            ThemeManagerService.Watch();
            AppRuntime.ThemeManagerService = ThemeManagerService;

            InitializeComponent();

            SourceInitialized += MainWindow_SourceInitialized;
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ApplicationThemeManager.Apply(ThemeManagerService.GetSysApplicationTheme(), ThemeManagerService.GetBackdropType(), true);
        }

        void IWindow.ShowForDownload(RunnerConfig runnerConfig)
        {
            throw new NotImplementedException();
        }
    }
}
