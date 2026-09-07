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

using ContractDownloadItemDto = PDownloader.Contracts.Downloads.DownloadItemDto;

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
    public event Action<DownloadItemViewModel>? OnProgress;
    public event Action<DownloadSettingsDto>? OnDownloadSettingsChanged;

    public void Handle(IpcReceivedMessage message)
    {
        if (message.TryGetPayload(
                DownloadProtocol.Progress,
                out ContractDownloadItemDto dto))
        {
            OnProgress?.Invoke(DownloadItemViewModel.FromContract(dto));
        }
        else if (message.TryGetPayload(
                     DownloadSettingsProtocol.Changed,
                     out DownloadSettingsDto settings))
        {
            OnDownloadSettingsChanged?.Invoke(settings);
        }
    }
}
