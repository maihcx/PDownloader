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

namespace PDownloader.Runner.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : IWindow
{
    public MainWindowViewModel ViewModel { get; }

    public Frame FrameHost => this.FrameHostContent;

    public ApplicationThemeManagerService ThemeManagerService { get; }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        ThemeManagerService = new ApplicationThemeManagerService(this);
        //WindowHelper.ThemeManagerService = ThemeManagerService;
        ThemeManagerService.InitCornerRadius();
        ThemeManagerService.Watch();
        AppRuntime.ThemeManagerService = ThemeManagerService;

        InitializeComponent();

        SourceInitialized += MainWindow_SourceInitialized;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplicationThemeManager.Apply(ThemeManagerService.GetSysApplicationTheme(), ThemeManagerService.GetBackdropType(), true);
    }

    void IWindow.ShowForDownload(RunnerConfig runnerConfig)
    {
        throw new NotImplementedException();
    }
}
