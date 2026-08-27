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

namespace PDownloader.Tray.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : INavigableView<MainWindowViewModels>, IDisposable
{
    public MainWindowViewModels ViewModel { get; }
    public ApplicationThemeManagerService ThemeManagerService { get; }

    private readonly PowerModeService _powerModeService = new PowerModeService();

    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModels();
        DataContext = this;
        AppRuntime.MainWindow = this;

        ThemeManagerService = new ApplicationThemeManagerService(this);
        AppRuntime.ThemeManagerService = ThemeManagerService;
        ThemeManagerService.InitCornerRadius();
        ThemeManagerService.Watch();

        TrayIcon.BalloonTipClicked += TrayIcon_BalloonTipClicked;
    }

    private void TrayIcon_BalloonTipClicked([System.Diagnostics.CodeAnalysis.NotNull] Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
    {
        _ = AppRuntime.CoreService?.SendAsync(
            AppProtocol.TrayEventMessage,
            AppProtocol.TrayEvent.GoSettingsUpdate);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        ApplicationThemeManager.Apply(ThemeManagerService.GetSysApplicationTheme(), ThemeManagerService.GetBackdropType(), true);

        this.Hide();
        _ = _powerModeService.OptimizeAfterAsync(TimeSpan.FromSeconds(3));

        _powerModeService.SetPowerMode(PowerModeService.PowerModeState.EfficiencyAdvanced);
    }

    private void NotifyIcon_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
    {
        AppRuntime.CoreService?.StartApp();
        _ = AppRuntime.CoreService?.SendAsync(
            AppProtocol.StateMessage,
            AppProtocol.State.Start);
    }

    public void ShowUpdateBalloon(string version)
    {
        string title = LocalizationHelper.GetLang("update_available_title");
        string body = $"PDownloader {version} {LocalizationHelper.GetLang("update_balloon_body")}";

        TrayIcon.ShowBalloonTip(TimeSpan.FromSeconds(5), title, body, Wpf.Ui.Tray.Controls.ToolTipIcon.Warning);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AppRuntime.CoreService!.IsAppStarted())
        {
            AppRuntime.CoreService.Send(
                AppProtocol.CoreServiceStateMessage,
                AppProtocol.State.Shutdown);
        }

        base.OnClosing(e);
    }

    private void TrayIcon_RightClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
    {
        _ = _powerModeService.OptimizeAfterAsync(TimeSpan.FromSeconds(3));
        _ = _powerModeService.OptimizeAfterAsync(TimeSpan.FromSeconds(20));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _powerModeService.Dispose();
            GC.SuppressFinalize(this);
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }
}
