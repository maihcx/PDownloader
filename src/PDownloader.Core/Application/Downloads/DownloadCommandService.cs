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
    public void Pause(string id) => DownloadManager.Instance.Pause(id);
    public void Resume(string id) => DownloadManager.Instance.Resume(id);
    public void Retry(string id) => DownloadManager.Instance.Retry(id);
    public void Cancel(string id) => DownloadManager.Instance.Cancel(id);
    public void Clear(DownloadClearScope scope) => DownloadManager.Instance.ClearAll(scope);
    public void PauseAll() => DownloadManager.Instance.PauseAll();
    public void ResumeAll() => DownloadManager.Instance.ResumeAll();
    public void RetryAll() => DownloadManager.Instance.RetryAll();
    public List<DownloadItemDto> GetList() => DownloadManager.Instance.GetContractList();
}
