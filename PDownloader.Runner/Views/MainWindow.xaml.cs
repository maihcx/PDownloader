// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Runner.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : IWindow
{
    private const double NormalWindowHeight = 370;
    private const double CompactWindowHeight = 300;
    private const double ExpandedWindowHeight = 450;
    private static readonly TimeSpan WindowResizeDuration = TimeSpan.FromMilliseconds(280);

    private enum WindowLayoutState
    {
        Normal,
        Compact,
        Expanded
    }

    private DownloaderProgressViewModel? _progressViewModel;
    private int _resizeAnimationVersion;

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

        Height = NormalWindowHeight;
        FrameHostContent.Navigated += FrameHostContent_Navigated;
        Closed += MainWindow_Closed;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplicationThemeManager.Apply(ThemeManagerService.GetSysApplicationTheme(), ThemeManagerService.GetBackdropType(), true);
    }

    private void FrameHostContent_Navigated(
        object sender,
        System.Windows.Navigation.NavigationEventArgs e)
    {
        DetachProgressViewModel();

        if (e.Content is DownloaderProgressPage progressPage)
        {
            _progressViewModel = progressPage.ViewModel;
            _progressViewModel.PropertyChanged += ProgressViewModel_PropertyChanged;

            SetWindowLayoutState(
                _progressViewModel.IsThreadVisualizationLayoutExpanded
                    ? WindowLayoutState.Expanded
                    : WindowLayoutState.Compact,
                animate: IsLoaded);
            return;
        }

        SetWindowLayoutState(WindowLayoutState.Normal, animate: IsLoaded);
    }

    private void ProgressViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(
                DownloaderProgressViewModel.IsThreadVisualizationLayoutExpanded)
            || sender is not DownloaderProgressViewModel viewModel)
        {
            return;
        }

        SetWindowLayoutState(
            viewModel.IsThreadVisualizationLayoutExpanded
                ? WindowLayoutState.Expanded
                : WindowLayoutState.Compact,
            animate: true);
    }

    private void SetWindowLayoutState(WindowLayoutState state, bool animate)
    {
        double targetHeight = state switch
        {
            WindowLayoutState.Normal => NormalWindowHeight,
            WindowLayoutState.Compact => CompactWindowHeight,
            WindowLayoutState.Expanded => ExpandedWindowHeight,
            _ => NormalWindowHeight
        };

        if (!IsLoaded || !animate)
        {
            StopResizeAnimation();
            Height = targetHeight;
            return;
        }

        double currentHeight = Height;
        if (double.IsNaN(currentHeight) || currentHeight <= 0)
        {
            currentHeight = ActualHeight;
        }

        if (Math.Abs(currentHeight - targetHeight) < 0.5)
        {
            return;
        }

        double currentTop = Top;
        double centerY = currentTop + (currentHeight / 2.0);
        double targetTop = centerY - (targetHeight / 2.0);

        StopResizeAnimation();
        Height = currentHeight;
        Top = currentTop;

        int animationVersion = ++_resizeAnimationVersion;
        var easing = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
        };

        var heightAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = currentHeight,
            To = targetHeight,
            Duration = new Duration(WindowResizeDuration),
            EasingFunction = easing,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };

        var topAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = currentTop,
            To = targetTop,
            Duration = new Duration(WindowResizeDuration),
            EasingFunction = easing,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };

        heightAnimation.Completed += (_, _) =>
        {
            if (animationVersion != _resizeAnimationVersion)
            {
                return;
            }

            BeginAnimation(HeightProperty, null);
            BeginAnimation(TopProperty, null);
            Height = targetHeight;
            Top = targetTop;
        };

        BeginAnimation(HeightProperty, heightAnimation);
        BeginAnimation(TopProperty, topAnimation);
    }

    private void StopResizeAnimation()
    {
        double currentHeight = Height;
        double currentTop = Top;

        _resizeAnimationVersion++;
        BeginAnimation(HeightProperty, null);
        BeginAnimation(TopProperty, null);

        if (!double.IsNaN(currentHeight) && currentHeight > 0)
        {
            Height = currentHeight;
        }

        if (!double.IsNaN(currentTop))
        {
            Top = currentTop;
        }
    }

    private void DetachProgressViewModel()
    {
        if (_progressViewModel == null)
        {
            return;
        }

        _progressViewModel.PropertyChanged -= ProgressViewModel_PropertyChanged;
        _progressViewModel = null;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        DetachProgressViewModel();
        FrameHostContent.Navigated -= FrameHostContent_Navigated;
    }
}
