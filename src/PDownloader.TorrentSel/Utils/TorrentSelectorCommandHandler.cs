// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

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
