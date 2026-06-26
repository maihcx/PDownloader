// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

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
