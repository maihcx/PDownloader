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

namespace PDownloader.Core.Application.App;

/// <summary>
/// Owns Core process lifecycle commands.
/// </summary>
public sealed class CoreLifecycleService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly CoreIpcHost _ipcHost;
    private readonly RunnerSessionManager _runnerSessions;

    public CoreLifecycleService(
        IHostApplicationLifetime lifetime,
        CoreIpcHost ipcHost,
        RunnerSessionManager runnerSessions)
    {
        _lifetime = lifetime;
        _ipcHost = ipcHost;
        _runnerSessions = runnerSessions;
    }

    public async Task HandleCoreStateAsync(
        AppState state,
        CancellationToken cancellationToken)
    {
        if (state != AppState.Shutdown)
        {
            return;
        }

        ConfluxService? main = _ipcHost.Main;
        if (main?.IsAppStarted() == true)
        {
            await main.SendAsync(
                AppProtocol.State,
                AppState.Shutdown,
                TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);
        }

        await _runnerSessions.ShutdownAllAsync().ConfigureAwait(false);
        _lifetime.StopApplication();
    }
}
