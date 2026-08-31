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

using Microsoft.Extensions.Hosting;

namespace PDownloader.Core.Service;

public class CoreBackgroundService : BackgroundService
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromMinutes(15);

    private readonly Bootstrap _bootstrap;
    private readonly CoreUpdateCoordinator _updateCoordinator;
    private readonly HttpBridgeService _httpBridge;
    private bool _bootstrapStarted;

    public CoreBackgroundService(
        Bootstrap bootstrap,
        CoreUpdateCoordinator updateCoordinator,
        HttpBridgeService httpBridge)
    {
        _bootstrap = bootstrap;
        _updateCoordinator = updateCoordinator;
        _httpBridge = httpBridge;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A downloaded update is consumed only when the long-running Core starts.
        if (_updateCoordinator.TryInstallPendingUpdateAtCoreStartup())
        {
            return;
        }

        _bootstrapStarted = true;
        await _bootstrap.OnStartedAsync(stoppingToken).ConfigureAwait(false);

        // Start HTTP bridge for browser extension (localhost:6287)
        try { _httpBridge.Start(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Core] HttpBridge failed: {ex.Message}");
        }

        using var updateTimer = new PeriodicTimer(UpdateCheckInterval);

        try
        {
            // Check immediately at Core startup. The timer below is only the
            // recurring fallback for updates released while Core is running.
            await _updateCoordinator.RunAutomaticUpdateAsync(stoppingToken);

            while (await updateTimer.WaitForNextTickAsync(stoppingToken))
            {
                await _updateCoordinator.RunAutomaticUpdateAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _httpBridge.Stop();
        if (_bootstrapStarted)
        {
            await _bootstrap.OnStoppedAsync().ConfigureAwait(false);
            _bootstrapStarted = false;
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
