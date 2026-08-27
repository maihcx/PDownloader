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

namespace PDownloader.Services.DownloadServices;

public class DownloadLauncherService
{
    public bool IsDaemonRunning => ConfluxManager.cfsPDownloaderCore != null;

    public void RequestDownload(string url, string saveTo = "", string fileName = "")
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string payload = JsonSerializer.Serialize(new StartDownloadRequest
        {
            Url = url,
            SaveTo = saveTo.Trim(),
            FileName = fileName.Trim()
        });

        ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.DownloadByLinkCommand, payload);
    }

    public void RefreshConfigs()
    {
        ConfluxManager.cfsPDownloaderCore?.Send("core-event", "refresh-downloader-configs");
    }
}
