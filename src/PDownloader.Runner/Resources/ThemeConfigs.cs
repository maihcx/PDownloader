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

namespace PDownloader.Runner.Resources;

public static class ThemeConfigs
{
    public static WindowBackdropType WindowBackdropDefault = WindowBackdropType.Mica;

    public static WindowBackdropType IWindowBackdropType { get => new WindowBackdropType(); }

    public enum IThemeType
    {
        //
        // Summary:
        //     Auto application theme.
        Auto,
        //
        // Summary:
        //     Light application theme.
        Light,
        //
        // Summary:
        //     Dark application theme.
        Dark,
        //
        // Summary:
        //     High contract application theme.
        HighContrast,
        //
        // Summary:
        //     Unknown application theme.
        Unknown,
    }
}
