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

namespace PDownloader.Core.Application.Downloads;

/// <summary>
/// Publishes download snapshots to the Main UI and the Runner that owns the
/// corresponding session. DownloadManager itself remains IPC-agnostic.
/// </summary>
public sealed class DownloadProgressPublisher
{
    private readonly CoreIpcHost _ipcHost;
    private readonly RunnerSessionManager _runnerSessions;
    private readonly ConcurrentDictionary<string, object> _broadcastLocks = new();

    public DownloadProgressPublisher(
        CoreIpcHost ipcHost,
        RunnerSessionManager runnerSessions)
    {
        _ipcHost = ipcHost;
        _runnerSessions = runnerSessions;
    }

    public void Publish(DownloadItem item)
    {
        object broadcastLock = _broadcastLocks.GetOrAdd(
            item.Id,
            static _ => new object());

        lock (broadcastLock)
        {
            DownloadItemDto dto = DownloadManager.ToContract(item);
            // MainReady/health adopts the exact Main process. A closed UI must
            // not cause a connection timeout on every download progress event.
            if (_ipcHost.Main is { } main && main.IsAppStarted())
                main.Send(DownloadProtocol.Progress, dto);

            if (_runnerSessions.TryGet(item.Id, out RunnerSession? session)
                && session.IsReady)
            {
                session.Channel.Send(DownloadProtocol.Progress, dto);
            }
        }
    }
}
