using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PDownloader.Installer.Utils
{
    /// <summary>
    /// Trả về key của Style cho Ellipse (dot) dựa trên StepIndex hiện tại
    /// so với step index của item (ConverterParameter).
    ///   active  → StepDotActive
    ///   done    → StepDotDone
    ///   pending → StepDot
    /// </summary>
    public class StepDotStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not int current || parameter is not string paramStr
                || !int.TryParse(paramStr, out int target))
                return DependencyProperty.UnsetValue;

            string key = current == target ? "StepDotActive"
                       : current > target  ? "StepDotDone"
                                           : "StepDot";

            return System.Windows.Application.Current.MainWindow?.FindResource(key)
                   ?? DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }

    /// <summary>
    /// Trả về key của Style cho TextBlock nhãn step trong sidebar.
    ///   active  → SideLabelActive
    ///   others  → SideLabel
    /// </summary>
    public class SideLabelStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not int current || parameter is not string paramStr
                || !int.TryParse(paramStr, out int target))
                return DependencyProperty.UnsetValue;

            string key = current == target ? "SideLabelActive" : "SideLabel";

            return System.Windows.Application.Current.MainWindow?.FindResource(key)
                   ?? DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }

    /// <summary>
    /// bool → Visibility (True = Visible, False = Collapsed) – dùng cho IsUninstallMode
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Visible;
    }

    /// <summary>
    /// Inverted: True = Collapsed, False = Visible
    /// </summary>
    public class BoolToVisibilityInvertedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Collapsed;
    }
}
