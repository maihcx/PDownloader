namespace PDownloader.Tray.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : INavigableView<MainWindowViewModels>
    {
        public MainWindowViewModels ViewModel { get; }
        public ApplicationThemeManagerService ThemeManagerService { get; }

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainWindowViewModels();
            DataContext = this;
            AppRuntime.MainWindow = this;

            ThemeManagerService = new ApplicationThemeManagerService(this);
            AppRuntime.ThemeManagerService = ThemeManagerService;
            ThemeManagerService.InitCornerRadius();
            ThemeManagerService.Watch();

            TrayIcon.BalloonTipClicked += TrayIcon_BalloonTipClicked;
        }

        private void TrayIcon_BalloonTipClicked([System.Diagnostics.CodeAnalysis.NotNull] Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
        {
            AppRuntime.CoreService?.Send("tray-event", "OnGoSettings--UPDATE");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ApplicationThemeManager.Apply(ThemeManagerService.GetSysApplicationTheme(), ThemeManagerService.GetBackdropType(), true);

            this.Hide();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void NotifyIcon_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
        {
            AppRuntime.CoreService?.StartApp();
            AppRuntime.CoreService?.Send("state", "start");
        }

        public void ShowUpdateBalloon(string version)
        {
            string title = LocalizationHelper.GetLang("update_available_title");
            string body = $"PDownloader {version} {LocalizationHelper.GetLang("update_balloon_body")}";

            TrayIcon.ShowBalloonTip(TimeSpan.FromSeconds(5), title, body, Wpf.Ui.Tray.Controls.ToolTipIcon.Warning);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (AppRuntime.CoreService!.IsAppStarted())
            {
                AppRuntime.CoreService.Send("core-svc-state", "shutdown");
            }
            base.OnClosing(e);
        }
    }
}
