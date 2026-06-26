namespace PDownloader.Views.PagesBottom
{
    [PageMeta("page_settings_title", "page_settings_summary", SymbolRegular.Settings24, 999)]
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            ViewModel.ScrollToUpdateRequested += OnScrollToUpdateRequested;
        }

        private async void OnScrollToUpdateRequested()
        {
            if (!SharedMem.IsScrollToUpdateCard)
            {
                return;
            }

            await Task.Delay(100);

            ScrollViewer? scrollViewer =
                VisualHelper.FindParent<ScrollViewer>(UpdateCard);

            if (scrollViewer == null)
                return;

            System.Windows.Point point = UpdateCard
                .TransformToVisual((Visual)scrollViewer.Content)
                .Transform(new System.Windows.Point(0, 0));

            double targetOffset = point.Y - 12;

            DoubleAnimation animation = new()
            {
                From = scrollViewer.VerticalOffset,
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(700),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            scrollViewer.BeginAnimation(
                SmoothScrollBehavior.AnimatedVerticalOffsetProperty,
                animation);
        }
    }

    public static class VisualHelper
    {
        public static T? FindParent<T>(DependencyObject child)
            where T : DependencyObject
        {
            DependencyObject parent =
                VisualTreeHelper.GetParent(child);

            while (parent != null)
            {
                if (parent is T typedParent)
                    return typedParent;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }
    }
}
