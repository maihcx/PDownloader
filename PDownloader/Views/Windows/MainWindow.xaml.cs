namespace PDownloader.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public ApplicationThemeManagerService ThemeManagerService { get; }

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            ThemeManagerService = new ApplicationThemeManagerService(this);
            WindowHelper.ThemeManagerService = ThemeManagerService;
            ThemeManagerService.InitCornerRadius();
            ThemeManagerService.Watch();

            InitializeComponent();
            SetPageService(navigationViewPageProvider);

            navigationService.SetNavigationControl(RootNavigation);
            RootNavigation.Navigated += RootNavigation_Navigated;

            this.SourceInitialized += OnSourceInitialized;
            this.Closing += MainWindow_Closing;

            WindowHelper.OnAutoHideNavChanged += SharedVariable_OnAutoHideNavChanged;

            SnackbarService snackbarService = new SnackbarService();
            snackbarService.SetSnackbarPresenter(GlobalSnackbar);
            WindowHelper.GlobalSnackbar = snackbarService;

            //TranslationSource.Instance.PropertyChanged += (s, e) =>
            //{
            //    RootNavigation.UpdateBreadcrumbContents();
            //};

            RestoreWindow();
        }

        private void RootNavigation_Navigated(NavigationView sender, NavigatedEventArgs args)
        {
            if (args?.Page is not FrameworkElement page)
                return;

            var pageType = page.GetType();

            var metaAttr = pageType.GetCustomAttributes(typeof(PageMetaAttribute), true)
                                   .FirstOrDefault() as PageMetaAttribute;

            if (metaAttr != null && metaAttr.IsShowPageTitle)
            {
                BreadcrumbBar.Visibility = Visibility.Visible;
                BreadcrumbBarHolder.Visibility = Visibility.Collapsed;
            }
            else
            {
                BreadcrumbBar.Visibility = Visibility.Collapsed;
                BreadcrumbBarHolder.Visibility = Visibility.Visible;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            ApplicationThemeManager.Apply(ThemeManagerService.GetSysApplicationTheme(), ThemeManagerService.GetBackdropType(), true);
            ViewModel.OnNavigatedTo();

            RootNavigation.IsPaneOpen = false;

            if (WindowHelper.IsAutoHideNavPanel)
            {
                this.SizeChanged += MainWindow_SizeChanged;
                MainWindow_SizeChanged(null, null);
            }

            //ThemeManagerService.OnThemeChanged += (theme) =>
            //{
            //    Wpf.Ui.Appearance.Theme.Apply(theme, ThemeManagerService.GetBackdropType(), true);
            //};
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

        private void RestoreWindow()
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
        }

        public void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs? e)
        {
            double size_width = this.Width;
            if (size_width < 900 && RootNavigation.IsPaneOpen)
            {
                RootNavigation.IsPaneOpen = false;
            }
            else if (size_width >= 900 && !RootNavigation.IsPaneOpen)
            {
                RootNavigation.IsPaneOpen = true;
            }
        }

        private void SharedVariable_OnAutoHideNavChanged(bool state)
        {
            if (state)
            {
                this.SizeChanged += MainWindow_SizeChanged;
                MainWindow_SizeChanged(null, null);
            }
            else
            {
                this.SizeChanged -= MainWindow_SizeChanged;
                RootNavigation.IsPaneOpen = true;
            }
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            System.Windows.Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }
    }
}
