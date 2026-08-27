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

namespace PDownloader.Views.PagesBottom;

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
        {
            return;
        }

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
            {
                return typedParent;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
}
