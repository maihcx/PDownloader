// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Runner.Helpers;

public class WidthToColumnsConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (values.Length < 4
            || !int.TryParse(values[3]?.ToString(), out int itemCount)
            || itemCount <= 0)
        {
            return 1;
        }

        if (!int.TryParse(values[2]?.ToString(), out int maxColumns)
            || maxColumns <= 0)
        {
            maxColumns = 4;
        }

        int desiredColumns = itemCount switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 2,
            5 or 6 => 3,
            7 or 8 => 4,
            _ => Math.Min(maxColumns, itemCount),
        };

        desiredColumns = Math.Min(desiredColumns, maxColumns);

        // For larger collections, avoid a single orphan card on the last row
        // when another column count can produce a more balanced layout.
        if (itemCount > 8
            && desiredColumns > 2
            && itemCount % desiredColumns == 1)
        {
            for (int candidate = desiredColumns - 1; candidate >= 2; candidate--)
            {
                if (itemCount % candidate != 1)
                {
                    desiredColumns = candidate;
                    break;
                }
            }
        }

        // Item count decides the preferred layout first. Width is only a
        // safety limit so the converter does not unexpectedly turn 5 items
        // into a 2-column layout while there is enough room for 3 columns.
        if (values[0] is double width
            && width > 0
            && double.TryParse(
                values[1]?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double minCardWidth)
            && minCardWidth > 0)
        {
            int availableColumns = Math.Clamp(
                (int)Math.Floor(width / minCardWidth),
                1,
                maxColumns);

            desiredColumns = Math.Min(desiredColumns, availableColumns);
        }

        return Math.Max(1, desiredColumns);
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
