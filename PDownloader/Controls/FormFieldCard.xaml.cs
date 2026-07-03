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

public class FormFieldCard : ContentControl
{
    public FormFieldCard()
    {
        IsInvisibleDescriptionText = true;
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
       nameof(Icon),
       typeof(SymbolIcon),
       typeof(FormFieldCard),
       new PropertyMetadata(null)
   );

    public static readonly DependencyProperty PrimaryTextProperty = DependencyProperty.Register(
        nameof(PrimaryText),
        typeof(string),
        typeof(FormFieldCard),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty DescriptionTextProperty = DependencyProperty.Register(
        nameof(DescriptionText),
        typeof(string),
        typeof(FormFieldCard),
        new PropertyMetadata(null, OnDescriptionTextChanged)
    );

    public static readonly DependencyProperty IsInvisibleDescriptionTextProperty = DependencyProperty.Register(
        nameof(IsInvisibleDescriptionText),
        typeof(bool),
        typeof(FormFieldCard),
        new PropertyMetadata(false));

    public string? PrimaryText
    {
        get => (string)GetValue(PrimaryTextProperty);
        set => SetValue(PrimaryTextProperty, value);
    }

    public string? DescriptionText
    {
        get => (string?)GetValue(DescriptionTextProperty);
        set => SetValue(DescriptionTextProperty, value);
    }

    private static void OnDescriptionTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FormFieldCard control)
        {
            var newText = e.NewValue as string;
            control.IsInvisibleDescriptionText = string.IsNullOrWhiteSpace(newText);
        }
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool? IsInvisibleDescriptionText
    {
        get => (bool?)GetValue(IsInvisibleDescriptionTextProperty);
        set => SetValue(IsInvisibleDescriptionTextProperty, value);
    }
}
