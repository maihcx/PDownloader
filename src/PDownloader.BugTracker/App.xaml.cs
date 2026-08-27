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

using System.Windows;

namespace PDownloader.BugTracker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        string? logPath = null;
        string? appName = null;

        // Parse args: --crash-report "<path>" [--app "PDownloader"]
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--crash-report" && i + 1 < e.Args.Length)
            {
                logPath = e.Args[++i];
            }
            else if (e.Args[i] == "--app" && i + 1 < e.Args.Length)
            {
                appName = e.Args[++i];
            }
        }

        var window = new CrashWindow(logPath, appName ?? "PDownloader");
        window.Show();
    }
}
