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
    private readonly Bootstrap _bootstrap;
    private readonly HttpBridgeService _httpBridge = new();

    public CoreBackgroundService(Bootstrap bootstrap)
    {
        _bootstrap = bootstrap;
        AppRuntime.bootstrap = bootstrap;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _bootstrap.OnStarted();

        // Start HTTP bridge for browser extension (localhost:6287)
        try { _httpBridge.Start(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Core] HttpBridge failed: {ex.Message}");
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException) { }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _httpBridge.Stop();
        _bootstrap.OnStopped();
        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        base.Dispose();
        _httpBridge.Dispose();
    }
}
