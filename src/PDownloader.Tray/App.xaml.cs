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

using System.Windows.Interop;
using System.Windows.Media;

namespace PDownloader.Tray;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private string logFile;
    public App()
    {
        string? appPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(appPath))
        {
            appPath = AppDomain.CurrentDomain.BaseDirectory;
        }
        else
        {
            appPath = Path.GetDirectoryName(appPath) ?? appPath;
        }

        logFile = Path.Combine(appPath, "crashTray.log");

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        RenderOptions.ProcessRenderMode = RenderMode.Default;
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        await UserDataStore.InitializeAsync();
        TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();

        MainWindow mainWindow = new MainWindow();
        mainWindow.Show();
    }

    public void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        File.AppendAllText(logFile, $"[{DateTime.Now}] UnhandledException: {ex}\n");
    }

    public void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        File.AppendAllText(logFile, $"[{DateTime.Now}] UnobservedTaskException: {e.Exception}\n");
        e.SetObserved();
    }
}
