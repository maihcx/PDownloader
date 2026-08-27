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

namespace PDownloader.Services.HostServices;

public class PowerModeHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    private IWindow? mainWindow = null;

    private PowerModeService? powerModeService = null;

    public PowerModeHostService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        mainWindow?.Activated -= MainWindow_Activated;

        mainWindow?.Deactivated -= MainWindow_Deactivated;

        mainWindow?.StateChanged -= MainWindow_StateChanged;

        return Task.CompletedTask;
    }

    private Task HandleActivationAsync()
    {
        if (mainWindow == null)
        {
            mainWindow = _serviceProvider.GetRequiredService<IWindow>();
        }

        if (powerModeService == null)
        {
            powerModeService = _serviceProvider.GetRequiredService<PowerModeService>();
        }

        mainWindow?.Activated += MainWindow_Activated;

        mainWindow?.Deactivated += MainWindow_Deactivated;

        mainWindow?.StateChanged += MainWindow_StateChanged;

        ApplicationThemeManager.Changed += (currentApplicationTheme, systemAccent) =>
        {
            _ = powerModeService.OptimizeAfterAsync(TimeSpan.FromSeconds(2));
        };

        return Task.CompletedTask;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (mainWindow?.WindowState == WindowState.Minimized)
        {
            powerModeService?.SetPowerMode(PowerModeService.PowerModeState.EfficiencyAdvanced);
        }
        else
        {
            powerModeService?.SetPowerMode(PowerModeService.PowerModeState.Normal);
            _ = powerModeService?.OptimizeAfterAsync(TimeSpan.FromSeconds(2));
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (mainWindow?.WindowState != WindowState.Minimized)
        {
            powerModeService?.SetPowerMode(PowerModeService.PowerModeState.Efficiency);
        }
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (mainWindow?.WindowState != WindowState.Minimized)
        {
            powerModeService?.SetPowerMode(PowerModeService.PowerModeState.Normal);
        }
    }
}
