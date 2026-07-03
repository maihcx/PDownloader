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

using PDownloader.Installer.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace PDownloader.Installer.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(bool uninstallMode = false)
    {
        DataContext = new InstallerViewModel(uninstallMode);
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
