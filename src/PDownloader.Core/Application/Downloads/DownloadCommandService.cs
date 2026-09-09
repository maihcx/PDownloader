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
/// Application facade for download control use-cases. IPC and HTTP adapters do
/// not talk to the DownloadManager singleton directly.
/// </summary>
public sealed class DownloadCommandService
{
    private readonly DownloadManager _downloads;
    private readonly DownloadProgressPublisher _progress;
    private readonly TorrentWorkflowService _torrentWorkflow;

    public DownloadCommandService(
        DownloadManager downloads,
        DownloadProgressPublisher progress,
        TorrentWorkflowService torrentWorkflow)
    {
        _downloads = downloads;
        _progress = progress;
        _torrentWorkflow = torrentWorkflow;
    }

    public void PublishRunnerSnapshot(RunnerSession session)
    {
        // Register this exact ready session and seed its async mailbox with the
        // current state, including transfers completed before Runner opened.
        _progress.AttachRunner(session);
    }

    public Task PauseAsync(string id, CancellationToken token) => _downloads.PauseAsync(id, token);
    public async Task ResumeAsync(string id, CancellationToken token)
    {
        DownloadItem? item = _downloads.Find(id);
        bool isTorrent = item?.DownloadKind == DownloadKind.Torrent;
        await _downloads.ResumeAsync(
            id,
            isShowRunner: !isTorrent,
            cancellationToken: token).ConfigureAwait(false);

        if (isTorrent)
        {
            await _torrentWorkflow.EnsureProgressShellAsync(id, token)
                .ConfigureAwait(false);
        }
    }
    public Task RetryAsync(string id, CancellationToken token) => _downloads.RetryAsync(id, token);
    public Task CancelAsync(string id, CancellationToken token) => _downloads.CancelAsync(id, token);
    public Task ClearAsync(DownloadClearScope scope, CancellationToken token) => _downloads.ClearAllAsync(scope, token);
    public Task PauseAllAsync(CancellationToken token) => _downloads.PauseAllAsync(token);
    public Task ResumeAllAsync(CancellationToken token)
    {
        string[] pausedIds = _downloads.GetAll()
            .Where(item => item.Status == DownloadStatus.Paused)
            .Select(item => item.Id)
            .ToArray();
        return Task.WhenAll(pausedIds.Select(id => ResumeAsync(id, token)));
    }
    public Task RetryAllAsync(CancellationToken token) => _downloads.RetryAllAsync(token);
    public List<DownloadItemDto> GetList() => _downloads.GetContractList();
}
