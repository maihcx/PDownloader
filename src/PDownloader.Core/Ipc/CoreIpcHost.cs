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

namespace PDownloader.Core.Ipc;

/// <summary>
/// Owns the long-lived Core IPC endpoints. Business services depend on this
/// small endpoint registry instead of process-wide static state.
/// </summary>
public sealed class CoreIpcHost
{
    public ConfluxService? Main { get; private set; }
    public ConfluxService? Tray { get; private set; }

    public void AttachMain(ConfluxService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Main = service;
    }

    public void AttachTray(ConfluxService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Tray = service;
    }

    public async Task StopAsync()
    {
        ConfluxService? main = Main;
        ConfluxService? tray = Tray;
        Main = null;
        Tray = null;

        if (main is not null)
        {
            await StopAndDisposeAsync(main).ConfigureAwait(false);
        }

        if (tray is not null)
        {
            await StopAndDisposeAsync(tray).ConfigureAwait(false);
        }
    }

    private static async Task StopAndDisposeAsync(ConfluxService service)
    {
        try
        {
            await service.StopServiceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Core IPC] Failed to stop endpoint: {ex.Message}");
        }
        finally
        {
            service.Dispose();
        }
    }
}
