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

namespace PDownloader.Downloads.Models;

internal static class DownloadItemContractMapper
{
    public static DownloadItemDto From(DownloadItem item) => new()
    {
        Id = item.Id,
        Url = item.Url,
        FileName = item.FileName,
        SavePath = item.SavePath,
        StartTime = item.StartTime,
        EndTime = item.EndTime,
        TotalBytes = item.TotalBytes,
        DownloadedBytes = item.DownloadedBytes,
        SpeedBps = item.SpeedBps,
        Progress = item.Progress,
        Status = item.Status,
        SpeedFormatted = item.SpeedFormatted,
        EtaFormatted = item.EtaFormatted,
        TotalFormatted = item.TotalFormatted,
        DownloadedFormatted = item.DownloadedFormatted,
        ErrorMessage = item.ErrorMessage,
        IsActive = item.IsActive,
        ProgressVisualizationMode = item.ProgressVisualizationMode,
        ProgressVisualizationStage = item.ProgressVisualizationStage,
        ThreadProgress = item.GetThreadProgressSnapshot().ToList(),
        IsMergeProgressActive = item.IsMergeProgressActive,
        Md5Hash = item.Md5Hash,
        Sha1Hash = item.Sha1Hash,
        Sha256Hash = item.Sha256Hash,
        FileMergeMode = item.MergeMode,
        CanPause = item.CanPause,
        CanResume = item.CanResume
    };
}
