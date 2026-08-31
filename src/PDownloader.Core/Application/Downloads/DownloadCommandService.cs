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

    public DownloadCommandService(DownloadManager downloads, DownloadProgressPublisher progress)
    {
        _downloads = downloads;
        _progress = progress;
    }

    public void PublishRunnerSnapshot(RunnerSession session)
    {
        // Register this exact ready session and seed its async mailbox with the
        // current state, including transfers completed before Runner opened.
        _progress.AttachRunner(session);
    }

    public void Pause(string id) => _downloads.Pause(id);
    public void Resume(string id) => _downloads.Resume(id);
    public void Retry(string id) => _downloads.Retry(id);
    public void Cancel(string id) => _downloads.Cancel(id);
    public void Clear(DownloadClearScope scope) => _downloads.ClearAll(scope);
    public void PauseAll() => _downloads.PauseAll();
    public void ResumeAll() => _downloads.ResumeAll();
    public void RetryAll() => _downloads.RetryAll();
    public List<DownloadItemDto> GetList() => _downloads.GetContractList();
}
