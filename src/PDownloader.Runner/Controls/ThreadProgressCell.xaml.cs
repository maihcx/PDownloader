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

using System.Windows.Media.Animation;

namespace PDownloader.Runner.Controls;

public partial class ThreadProgressCell : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(ThreadProgressCell),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SpeedTextProperty =
        DependencyProperty.Register(
            nameof(SpeedText),
            typeof(string),
            typeof(ThreadProgressCell),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BytesTextProperty =
        DependencyProperty.Register(
            nameof(BytesText),
            typeof(string),
            typeof(ThreadProgressCell),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProgressTextProperty =
        DependencyProperty.Register(
            nameof(ProgressText),
            typeof(string),
            typeof(ThreadProgressCell),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(ThreadProgressCell),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State),
            typeof(string),
            typeof(ThreadProgressCell),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty OverallStatusProperty =
        DependencyProperty.Register(
            nameof(OverallStatus),
            typeof(DownloadStatus),
            typeof(ThreadProgressCell),
            new PropertyMetadata(DownloadStatus.Connecting));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(
            nameof(IsIndeterminate),
            typeof(bool),
            typeof(ThreadProgressCell),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public ThreadProgressCell()
    {
        InitializeComponent();

        Loaded += (_, _) => UpdateVisual(animate: false);
        SizeChanged += (_, _) => UpdateProgressWidth(animate: false);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string SpeedText
    {
        get => (string)GetValue(SpeedTextProperty);
        set => SetValue(SpeedTextProperty, value);
    }

    public string BytesText
    {
        get => (string)GetValue(BytesTextProperty);
        set => SetValue(BytesTextProperty, value);
    }

    public string ProgressText
    {
        get => (string)GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string State
    {
        get => (string)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public DownloadStatus OverallStatus
    {
        get => (DownloadStatus)GetValue(OverallStatusProperty);
        set => SetValue(OverallStatusProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    private static void OnVisualPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is ThreadProgressCell control && control.IsLoaded)
        {
            control.UpdateVisual(animate: true);
        }
    }

    private void UpdateVisual(bool animate)
    {
        UpdateProgressWidth(animate);
    }

    private void UpdateProgressWidth(bool animate)
    {
        if (!IsLoaded || ProgressSurface.ActualWidth <= 0)
        {
            return;
        }

        ThreadCellState visualState = GetVisualState(State);
        double normalizedProgress = visualState == ThreadCellState.Completed
            ? 1.0
            : IsIndeterminate
                ? 0.0
                : Math.Clamp(Progress, 0.0, 100.0) / 100.0;

        double targetWidth = ProgressSurface.ActualWidth * normalizedProgress;

        if (!animate)
        {
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = targetWidth;
            return;
        }

        DoubleAnimation animation = new()
        {
            From = ProgressFill.ActualWidth,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        ProgressFill.BeginAnimation(
            WidthProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static ThreadCellState GetVisualState(string? state)
    {
        if (string.Equals(state, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return ThreadCellState.Completed;
        }

        if (string.Equals(state, "Downloading", StringComparison.OrdinalIgnoreCase))
        {
            return ThreadCellState.Downloading;
        }

        // Waiting, Connecting and Retrying all represent the connection/preparation phase.
        return ThreadCellState.Connecting;
    }

    private enum ThreadCellState
    {
        Connecting,
        Downloading,
        Completed
    }
}
