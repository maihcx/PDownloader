namespace PDownloader.Views.Windows
{
    public partial class MainWindow : IWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public ApplicationThemeManagerService ThemeManagerService { get; }

        BreadcrumbBar IWindow.BreadcrumbBar => BreadcrumbBar;

        BreadcrumbBar IWindow.BreadcrumbBarHolder => BreadcrumbBarHolder;

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationService navigationService,
            IServiceProvider serviceProvider,
            ISnackbarService snackbarService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            ThemeManagerService = new ApplicationThemeManagerService(this);
            WindowHelper.ThemeManagerService = ThemeManagerService;
            ThemeManagerService.InitCornerRadius();
            ThemeManagerService.SetApplicationTheme(ThemeManagerService.GetApplicationTheme());
            this.WindowBackdropType = ThemeManagerService.GetBackdropType();

            InitializeComponent();

            snackbarService.SetSnackbarPresenter(GlobalSnackbar);
            navigationService.SetNavigationControl(RootNavigation);

            TranslationSource.Instance.PropertyChanged += (s, e) =>
            {
                RootNavigation.UpdateBreadcrumbContents();
            };

            SetupWindowSize();
        }

        public void ShowWithEffect()
        {
            this.Opacity = 0;
            this.ShowInTaskbar = true;
            this.Show();

            // Fade in
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            this.BeginAnimation(Window.OpacityProperty, fade);

            // Scale in
            var scaleAnim = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            RootScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            RootScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            this.Activate();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindow();
        }

        private void SaveWindow()
        {
            UserDataStore.SetValue("IsWindow_Maximized", this.WindowState == WindowState.Maximized);
            UserDataStore.SetValue("Window_Top", this.Top);
            UserDataStore.SetValue("Window_Left", this.Left);
            UserDataStore.SetValue("Window_Width", this.Width);
            UserDataStore.SetValue("Window_Height", this.Height);
            UserDataStore.SetValue("StartUpCode", "xv2");
        }

        private void SetupWindowSize()
        {
            string startUpCode = UserDataStore.GetValue<string>("StartUpCode");
            if (startUpCode != "xv1")
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;

                this.Top = UserDataStore.GetValue<double>("Window_Top");
                this.Left = UserDataStore.GetValue<double>("Window_Left");
                this.Width = UserDataStore.GetValue<double>("Window_Width");
                this.Height = UserDataStore.GetValue<double>("Window_Height");

                if (UserDataStore.GetValue<bool>("IsWindow_Maximized"))
                {
                    this.WindowState = WindowState.Maximized;
                }
                else
                {
                    this.WindowState = WindowState.Normal;
                }
            }
            else
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            }

            this.Closing += MainWindow_Closing;
        }
    }
}
