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

namespace PDownloader.TorrentSel.Utils;

public static class TorrentSelectorCommandHandler
{
    public static void HandleMainEvent(MainAppEvent mainEvent)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            switch (mainEvent)
            {
                case MainAppEvent.LanguageChanged:
                    UserDataStore.Reload();
                    TranslationSource.Instance.CurrentCulture =
                        LanguageBase.GetSetupLanguage();
                    break;

                case MainAppEvent.AppExit:
                    Application.Current.Shutdown();
                    break;
            }
        }));
    }
}
