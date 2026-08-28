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

using PDownloader.Contracts.Ipc;

namespace PDownloader.Contracts.Downloads;

public static class DownloadProtocol
{
    public static readonly IpcRequestDefinition<IpcNoPayload, List<DownloadItemDto>> GetList =
        new("download.list.get");

    public static readonly IpcMessageDefinition<DownloadItemDto> Progress =
        new("download.progress");

    public static readonly IpcMessageDefinition<StartDownloadRequest> DownloadByLink =
        new("download.create");

    public static readonly IpcMessageDefinition<StartDownloadRequest> RunnerStartDownload =
        new("download.runner.start");

    public static readonly IpcMessageDefinition<DownloadIdRequest> RunnerPause =
        new("download.runner.pause");

    public static readonly IpcMessageDefinition<DownloadIdRequest> RunnerResume =
        new("download.runner.resume");

    public static readonly IpcMessageDefinition<DownloadIdRequest> RunnerRetry =
        new("download.runner.retry");

    public static readonly IpcMessageDefinition<DownloadIdRequest> RunnerCancel =
        new("download.runner.cancel");

    public static readonly IpcMessageDefinition<DownloadClearScope> RunnerClear =
        new("download.clear");

    public static readonly IpcMessageDefinition<IpcNoPayload> RunnerPauseAll =
        new("download.pause-all");

    public static readonly IpcMessageDefinition<IpcNoPayload> RunnerResumeAll =
        new("download.resume-all");

    public static readonly IpcMessageDefinition<IpcNoPayload> RunnerRetryAll =
        new("download.retry-all");

    public static readonly IpcMessageDefinition<IpcNoPayload> RunnerCancelExperience =
        new("runner.cancel-experience");

    public static readonly IpcMessageDefinition<IpcNoPayload> RunnerUiClosed =
        new("runner.ui-closed");

    public static readonly IpcMessageDefinition<StartDownloadRequest> RunnerDownload =
        new("runner.download");

    public static readonly IpcMessageDefinition<IpcNoPayload> RunnerCancelMessage =
        new("runner.cancel");
}

public sealed record DownloadIdRequest(string DownloadId);

public enum DownloadClearScope
{
    Completed,
    All
}
