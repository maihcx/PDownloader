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

using PDownloader.Runner.Resources;

namespace PDownloader.Runner.Utils;

/// <summary>
/// Applies typed application/lifecycle events sent by Core to Runner.
/// IPC deserialization and routing stay in ConfluxService.
/// </summary>
public static class RunnerCommandHandler
{
    public static void HandleState(AppState state)
    {
        if (state != AppState.Shutdown)
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            System.Windows.Application.Current?.Shutdown());
    }

    public static void HandleMainEvent(MainAppEvent mainEvent)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (mainEvent)
            {
                case MainAppEvent.LanguageChanged:
                    UserDataStore.Reload();
                    TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();
                    break;

                case MainAppEvent.RadiusChanged:
                    UserDataStore.Reload();
                    Application.Current.Resources["ControlCornerRadius"] =
                        new CornerRadius(UserDataStore.GetValue<int>("ObjectCornerRadius"));
                    break;

                case MainAppEvent.MaterialChanged:
                    UserDataStore.Reload();
                    AppRuntime.ThemeManagerService?.SetBackdropType(
                        Enum.Parse<WindowBackdropType>(
                            AppRuntime.ThemeManagerService.GetMaterialCBBSelected()?.Value ?? "Mica"));
                    AppRuntime.ThemeManagerService?.SetApplicationTheme(
                        Enum.Parse<ThemeConfigs.IThemeType>(
                            AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                    break;

                case MainAppEvent.ThemeChanged:
                    UserDataStore.Reload();
                    AppRuntime.ThemeManagerService?.SetApplicationTheme(
                        Enum.Parse<ThemeConfigs.IThemeType>(
                            AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                    break;

                case MainAppEvent.AppExit:
                    Application.Current.Shutdown();
                    break;
            }
        });
    }
}
