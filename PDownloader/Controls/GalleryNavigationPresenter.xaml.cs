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

namespace PDownloader.Controls;

public class GalleryNavigationPresenter : System.Windows.Controls.Control
{
    /// <summary>
    /// Property for <see cref="ItemsSource"/>.
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(object),
        typeof(GalleryNavigationPresenter),
        new PropertyMetadata(null)
    );

    /// <summary>
    /// Property for <see cref="TemplateButtonCommand"/>.
    /// </summary>
    public static readonly DependencyProperty TemplateButtonCommandProperty = DependencyProperty.Register(
        nameof(TemplateButtonCommand),
        typeof(IRelayCommand),
        typeof(GalleryNavigationPresenter),
        new PropertyMetadata(null)
    );

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets the command triggered after clicking the titlebar button.
    /// </summary>
    public IRelayCommand TemplateButtonCommand =>
        (IRelayCommand)GetValue(TemplateButtonCommandProperty);

    public static readonly DependencyProperty IsVerticalProperty = DependencyProperty.Register(
        nameof(IsVertical),
        typeof(bool),
        typeof(GalleryNavigationPresenter),
        new PropertyMetadata(true)
    );

    /// <summary>
    /// Initializes a new instance of the <see cref="GalleryNavigationPresenter"/> class.
    /// Creates a new instance of the class and sets the default <see cref="FrameworkElement.Loaded"/> event.
    /// </summary>
    public GalleryNavigationPresenter()
    {
        SetValue(TemplateButtonCommandProperty, new Wpf.Ui.Input.RelayCommand<Type>(o => OnTemplateButtonClick(o)));
    }

    private void OnTemplateButtonClick(Type? pageType)
    {
        INavigationService navigationService = App.GetRequiredService<INavigationService>();

        if (pageType is not null)
        {
            navigationService.Navigate(pageType);
        }
    }

    public bool IsVertical
    {
        get => (bool)GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }
}
