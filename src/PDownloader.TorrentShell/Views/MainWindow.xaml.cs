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

namespace PDownloader.TorrentShell.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        ViewModel.CloseRequested += ViewModel_CloseRequested;
        ContentRendered += MainWindow_ContentRendered;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
    }

    private void ViewModel_CloseRequested() => Close();

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        // Apply the same one-time activation as Runner after WPF has rendered.
        ContentRendered -= MainWindow_ContentRendered;
        WindowHelper.BringToFront(this);
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        WindowHelper.StopFlashing(this);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        ViewModel.CloseRequested -= ViewModel_CloseRequested;
        ContentRendered -= MainWindow_ContentRendered;
        Activated -= MainWindow_Activated;
        Closed -= MainWindow_Closed;
        Application.Current.Shutdown();
    }
}
