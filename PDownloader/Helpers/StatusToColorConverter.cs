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

using Color = System.Windows.Media.Color;

namespace PDownloader.Helpers;

internal class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            "Completed" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            "Error" => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
            "Paused" => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
            "Merging" => new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),
            _ => new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) // blue
        };
        //Debug.WriteLine(value?.GetType().FullName);

        //return System.Drawing.Brushes.Red;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
