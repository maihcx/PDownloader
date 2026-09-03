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

namespace PDownloader.Core.Utils;

/// <summary>One set of application defaults, independent of each UI's user.config.</summary>
internal static class UserDataDefaults
{
    public static Dictionary<string, JsonElement> Create() => new Dictionary<string, object?>
    {
        ["IWindowBackdropType"] = "Tabbed",
        ["IThemeType"] = "Auto",
        ["Window_Top"] = 0d,
        ["Window_Left"] = 0d,
        ["Window_Width"] = 719d,
        ["Window_Height"] = 556d,
        ["IsWindow_Maximized"] = false,
        ["StartUpCode"] = "xv1",
        ["IsAutoHideNavPanel"] = false,
        ["Language"] = "en",
        ["ObjectCornerRadius"] = 6,
        ["IsViewAtBoot"] = true,
        ["IsNavPaneOpen"] = false,
        ["IsAutoUpdateEnabled"] = true,
    }.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
}
