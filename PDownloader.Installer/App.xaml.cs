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

using PDownloader.Installer.Services;
using PDownloader.Installer.Views;
using System.Windows;

namespace PDownloader.Installer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isUninstall = e.Args.Contains("--uninstall");
        var window = new MainWindow(isUninstall);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        InstallService.ScheduleSelfExtractCleanup();
        base.OnExit(e);
    }
}
