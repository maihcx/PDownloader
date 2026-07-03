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

namespace PDownloader.Runner.Services;

/// <summary>
/// Managed host of the application.
/// </summary>
public class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider? _serviceProvider;

    private IWindow? _mainWindow;

    public ApplicationHostService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync();
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        _mainWindow = (
            _serviceProvider?.GetService(typeof(IWindow)) as IWindow
        )!;
        //WindowHelper.BringToFront(App.Current.MainWindow);
        _mainWindow!.Show();

        _mainWindow!.Activate();

        if (_mainWindow is MainWindow window && window.ViewModel is INavigationAware navigationAware)
        {
            await navigationAware.OnNavigatedToAsync();
        }

        await Task.CompletedTask;
    }
}
