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

namespace PDownloader.Helpers;

internal class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue || parameter is not string enumName)
        {
            return false;
        }

        string[] arreNumName = enumName.Split('~');
        bool isInvert = false;

        if (arreNumName.Length == 2)
        {
            if (arreNumName[0].Equals("!"))
            {
                isInvert = true;
            }

            enumName = arreNumName[1];
        }

        bool parseCompare = Enum.TryParse(
            enumValue.GetType(),
            enumName,
            ignoreCase: true,
            out var parsedValue)
            && Equals(enumValue, parsedValue);

        if (isInvert)
        {
            return !parseCompare;
        }

        return parseCompare;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
