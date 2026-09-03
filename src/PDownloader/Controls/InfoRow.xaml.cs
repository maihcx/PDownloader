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

public class InfoRow : ContentControl
{
    private Wpf.Ui.Controls.Button? _copyButton;

    public InfoRow()
    {

    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_copyButton != null)
        {
            _copyButton.Click -= CopyButton_Click;
        }

        _copyButton = GetTemplateChild("PART_CopyButton") as Wpf.Ui.Controls.Button;

        if (_copyButton != null)
        {
            _copyButton.Click += CopyButton_Click;
        }

        UpdateValueVisibility();
    }

    public static readonly DependencyProperty LabelTextProperty = DependencyProperty.Register(
        nameof(LabelText),
        typeof(string),
        typeof(InfoRow),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText),
        typeof(string),
        typeof(InfoRow),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty IsLastProperty = DependencyProperty.Register(
        nameof(IsLast),
        typeof(bool),
        typeof(InfoRow),
        new PropertyMetadata(false)
    );

    public static readonly DependencyProperty NavigateUriProperty = DependencyProperty.Register(
        nameof(NavigateUri),
        typeof(string),
        typeof(InfoRow),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register(
        nameof(LabelWidth),
        typeof(double),
        typeof(InfoRow),
        new PropertyMetadata((double)120)
    );

    public static readonly DependencyProperty ValueContentProperty = DependencyProperty.Register(
        nameof(ValueContent),
        typeof(object),
        typeof(InfoRow),
        new PropertyMetadata(null, OnValueContentChanged)
    );

    public static readonly DependencyProperty ValueHorizontalAlignProperty = DependencyProperty.Register(
        nameof(ValueHorizontalAlign),
        typeof(HorizontalAlignment),
        typeof(InfoRow),
        new PropertyMetadata(HorizontalAlignment.Left)
    );

    public string? LabelText
    {
        get => (string)GetValue(LabelTextProperty);
        set => SetValue(LabelTextProperty, value);
    }

    public string? ValueText
    {
        get => (string?)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public bool IsLast
    {
        get => (bool)GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    public string? NavigateUri
    {
        get => (string?)GetValue(NavigateUriProperty);
        set => SetValue(NavigateUriProperty, value);
    }

    public double? LabelWidth
    {
        get => (double)GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }

    public object? ValueContent
    {
        get => GetValue(ValueContentProperty);
        set => SetValue(ValueContentProperty, value);
    }

    public HorizontalAlignment ValueHorizontalAlign
    {
        get => (HorizontalAlignment)GetValue(ValueHorizontalAlignProperty);
        set => SetValue(ValueHorizontalAlignProperty, value);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        string? text = ValueContent as string ?? ValueText;
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
        }
    }

    private static void OnValueContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoRow row)
        {
            row.UpdateValueVisibility();
        }
    }

    private void UpdateValueVisibility()
    {
        var contentPresenter = GetTemplateChild("PART_ValueContent") as ContentPresenter;
        var valueText = GetTemplateChild("PART_ValueText") as FrameworkElement;
        var hyperlinkBtn = GetTemplateChild("PART_HyperlinkButton") as FrameworkElement;

        bool hasContent = ValueContent != null;

        contentPresenter?.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;

        valueText?.Visibility = hasContent ? Visibility.Collapsed : valueText.Visibility;

        hyperlinkBtn?.Visibility = hasContent ? Visibility.Collapsed : hyperlinkBtn.Visibility;
    }
}
