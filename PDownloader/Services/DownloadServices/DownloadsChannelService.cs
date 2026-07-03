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

/// <summary>
/// Receives CFS messages from Core and raises events for the ViewModel.
/// Registered in Bootstrap.OnBeforeStartup via cfsMain.OnMessageReceived.
/// </summary>
public class DownloadsChannelService
{
    public event Action<List<DownloadItemDto>>? OnList;
    public event Action<DownloadItemDto>? OnProgress;

    public void Handle(string name, string value)
    {
        switch (name)
        {
            case "muxt-get-downloader-list":
                HandleList(value);
                break;

            case "muxt-download-progress":
                HandleProgress(value);
                break;
        }
    }

    private void HandleList(string value)
    {
        try
        {
            List<DownloadItemDto>? list = JsonSerializer.Deserialize<List<DownloadItemDto>>(value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list != null)
            {
                OnList?.Invoke(list);
            }
        }
        catch { }
    }

    private void HandleProgress(string value)
    {
        try
        {
            DownloadItemDto? dto = JsonSerializer.Deserialize<DownloadItemDto>(value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto != null)
            {
                OnProgress?.Invoke(dto);
            }
        }
        catch { }
    }
}
