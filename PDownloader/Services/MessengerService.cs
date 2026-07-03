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

namespace PDownloader.Services;

public static class MessengerService
{
    private static readonly ISnackbarService GlobalSnackbar = App.GetRequiredService<ISnackbarService>();

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance)
    {
        ShowSnackbar(title, content, controlAppearance, null, default);
    }

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, TimeSpan timeSpan = default)
    {
        ShowSnackbar(title, content, controlAppearance, null, timeSpan);
    }

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, IconElement? icon = null)
    {
        ShowSnackbar(title, content, controlAppearance, icon, default);
    }

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, IconElement? icon = null, TimeSpan timeSpan = default)
    {
        GlobalSnackbar.Show(LanguageBase.GetLangValue(title), LanguageBase.GetLangValue(content), controlAppearance, icon, timeSpan);
    }
}
