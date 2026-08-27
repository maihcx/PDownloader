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

internal class BoolToStatusText : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (bool)value ? $"● {Resources.Locales.String.ResourceManager.GetString("page_config_svc_active_title", TranslationSource.Instance.CurrentCulture)}" : $"● {Resources.Locales.String.ResourceManager.GetString("page_config_svc_inactive_title", TranslationSource.Instance.CurrentCulture)}";
    }

    public object ConvertBack(object value, Type t, object? p, CultureInfo c)
    {
        throw new NotImplementedException();
    }
}
