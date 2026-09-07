// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.TorrentSel.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        ViewModel.CloseRequested += ViewModel_CloseRequested;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void ViewModel_CloseRequested() => Close();

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        Activate();
        Topmost = false;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        ViewModel.CloseRequested -= ViewModel_CloseRequested;
        Application.Current.Shutdown();
    }
}
