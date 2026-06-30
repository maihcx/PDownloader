using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PDownloader.Runner.Models;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace PDownloader.Runner.Helpers;

[ValueConversion(typeof(DownloadStatus), typeof(string))]
public class StatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (DownloadStatus)value switch
        {
            DownloadStatus.Queued      => "⏳",
            DownloadStatus.Connecting  => "🔗",
            DownloadStatus.Downloading => "⬇",
            DownloadStatus.Paused      => "⏸",
            DownloadStatus.Merging     => "🔧",
            DownloadStatus.Completed   => "✅",
            DownloadStatus.Error       => "❌",
            _                          => "?"
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

[ValueConversion(typeof(DownloadStatus), typeof(System.Windows.Media.Brush))]
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (DownloadStatus)value switch
        {
            DownloadStatus.Completed   => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            DownloadStatus.Error       => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
            DownloadStatus.Paused      => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
            DownloadStatus.Merging     => new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),
            _                          => new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) // blue
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // If parameter is a status string like "Completed", compare status
        if (value is DownloadStatus status && parameter is string s)
            return status.ToString() == s ? Visibility.Visible : Visibility.Collapsed;

        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Also support Count == 0 comparison for empty state
        if (value is int count && parameter is string s && int.TryParse(s, out int zero))
            return count == zero ? Visibility.Visible : Visibility.Collapsed;

        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Maps progress 0–100 to pixel width given the parent container ActualWidth.</summary>
public class ProgressToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is double pct && values[1] is double total)
            return Math.Max(0, pct / 100.0 * total);
        return 0.0;
    }
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => Array.Empty<object>();
}

public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType,
                          object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString()
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType,
                              object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType,
                          object parameter, CultureInfo culture)
    {
        if (values[0] is double pct && values[1] is double w)
            return Math.Max(0, Math.Min(w, w * pct / 100.0));
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes,
                                object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
