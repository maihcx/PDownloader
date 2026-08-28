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

namespace PDownloader.Runner.Services;

public class ApplicationThemeManagerService
{
    public WindowBackdropType GetBackdropType()
    {
        return (WindowBackdropType)Enum.Parse(
            typeof(WindowBackdropType),
            UserDataStore.GetValue<string>("IWindowBackdropType")
        );
    }

    public delegate void ThemeChangedHandle(ThemeType theme);

    public event ThemeChangedHandle? OnThemeChanged;

    public Window MainWindowHandle { get; private set; }

    public bool IsWatcher { get; set; }

    public ApplicationThemeManagerService(Window mainWindow)
    {
        MainWindowHandle = mainWindow;
        //ApplicationThemeManager.Changed += (ThemeType currentTheme, System.Windows.Media.Color systemAccent) =>
        //{
        //ThemeType themeType = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        //ApplicationSysTheme = themeService.GetTheme();

        //if (ApplicationThemeManager.IsMatchedDark() || (!ApplicationThemeManager.IsMatchedLight() && currentTheme == ThemeType.Light))
        //{
        //    ApplicationSysTheme = ThemeType.Dark;
        //}
        //else if (ApplicationThemeManager.IsMatchedLight() || (!ApplicationThemeManager.IsMatchedDark() && currentTheme == ThemeType.Dark))
        //{
        //    ApplicationSysTheme = ThemeType.Light;
        //}
        //};
    }

    public void SetBackdropType(WindowBackdropType _WindowBackdropType)
    {
        UserDataStore.SetValue("IWindowBackdropType", _WindowBackdropType.ToString());

        ThemeApply(GetSysApplicationTheme(), _WindowBackdropType);
    }

    public ThemeConfigs.IThemeType GetApplicationTheme()
    {
        try
        {
            return (ThemeConfigs.IThemeType)Enum.Parse(
                typeof(ThemeConfigs.IThemeType),
                UserDataStore.GetValue<string>("IThemeType")
            );
        }
        catch
        {
            return ThemeConfigs.IThemeType.Auto;
        }
    }

    public ThemeType GetSysApplicationTheme()
    {
        ThemeType _ThemeType;
        if (UserDataStore.GetValue<string>("IThemeType") == "Auto")
        {
            ApplicationThemeManager.ApplySystemTheme();
            _ThemeType = ApplicationThemeManager.GetAppTheme();
        }
        else
        {
            _ThemeType = (ThemeType)Enum.Parse(
                typeof(ThemeType),
                UserDataStore.GetValue<string>("IThemeType")
            );
        }

        return _ThemeType;
    }

    private int globalCornerRadius = UserDataStore.GetValue<int>("ObjectCornerRadius");
    public int GlobalCornerRadius
    {
        get => globalCornerRadius;
        set
        {
            if (globalCornerRadius == value)
            {
                return;
            }

            globalCornerRadius = value;

            System.Windows.Application.Current.Resources["ControlCornerRadius"] = new CornerRadius(value);
            UserDataStore.SetValue("ObjectCornerRadius", value);
        }
    }

    public void SetApplicationTheme(ThemeConfigs.IThemeType _IThemeType)
    {
        UnWatch();
        UserDataStore.SetValue("IThemeType", _IThemeType.ToString());
        ThemeType applicationTheme = GetSysApplicationTheme();
        WindowBackdropType windowBackdropType = GetBackdropType();

        if (_IThemeType == ThemeConfigs.IThemeType.Auto)
        {
            Watch(applicationTheme, windowBackdropType);
        }
        else
        {
            ThemeApply(applicationTheme, windowBackdropType);
        }

        OnThemeChanged?.Invoke(applicationTheme);
    }

    public void Watch(ThemeType applicationTheme = ThemeType.Unknown, WindowBackdropType windowBackdrop = WindowBackdropType.Mica, bool updateAccents = true)
    {
        if (!IsWatcher)
        {
            ThemeApply(applicationTheme, windowBackdrop);
            Watcher.Watch(Application.Current.MainWindow, windowBackdrop, updateAccents);
            SystemThemeWatcher.Watch(MainWindowHandle, this.GetBackdropType(), updateAccents);

            IsWatcher = true;
        }
    }

    private void ThemeApply(ThemeType applicationTheme = ThemeType.Light, WindowBackdropType backgroundEffect = WindowBackdropType.Mica)
    {
        ApplicationThemeManager.Apply(applicationTheme, backgroundEffect, true);
    }

    public void UnWatch()
    {
        if (IsWatcher)
        {
            Watcher.UnWatch(Application.Current.MainWindow);
            SystemThemeWatcher.UnWatch(MainWindowHandle);
            IsWatcher = false;
        }
    }

    public void InitCornerRadius()
    {
        System.Windows.Application.Current.Resources["ControlCornerRadius"] = new CornerRadius(GlobalCornerRadius);
    }

    public ObservableCollection<Models.ComboBoxItem> GetThemeCBBs()
    {
        return new ObservableCollection<Models.ComboBoxItem>(
            Enum.GetValues(typeof(ThemeConfigs.IThemeType))
                .Cast<ThemeConfigs.IThemeType>()
                .Where(e => e != ThemeConfigs.IThemeType.Unknown)
                .Select(e => new Models.ComboBoxItem
                {
                    Value = ((int)e).ToString(),
                    Content = e.ToString()
                })
        );
    }

    public Models.ComboBoxItem? GetThemeCBBSelected()
    {
        return GetThemeCBBs().FirstOrDefault(x => x.Content == GetApplicationTheme().ToString());
    }

    public ObservableCollection<Models.ComboBoxItem> GetMaterialCBBs()
    {
        return new ObservableCollection<Models.ComboBoxItem>(
            Enum.GetValues(typeof(WindowBackdropType))
                .Cast<WindowBackdropType>()
                .Select(e => new Models.ComboBoxItem
                {
                    Value = ((int)e).ToString(),
                    Content = e.ToString()
                })
        );
    }

    public Models.ComboBoxItem? GetMaterialCBBSelected()
    {
        return GetMaterialCBBs().FirstOrDefault(x => x.Content == GetBackdropType().ToString());
    }
}
