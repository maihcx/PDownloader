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

namespace PDownloader.Core.Download.Models;

public record DownloadItemDto(
    string Id, string Url, string FileName, string SavePath,
    DateTime StartTime, DateTime EndTime,
    long TotalBytes, long DownloadedBytes, double SpeedBps,
    double Progress, string Status,
    string SpeedFormatted, string EtaFormatted,
    string TotalFormatted, string DownloadedFormatted,
    string ErrorMessage, bool IsActive,
    string ProgressVisualizationMode,
    string ProgressVisualizationStage,
    IReadOnlyList<DownloadThreadProgress> ThreadProgress)
{
    public static DownloadItemDto From(DownloadItem i) => new(
        i.Id.ToString(), i.Url, i.FileName, i.SavePath,
        i.StartTime, i.EndTime,
        i.TotalBytes, i.DownloadedBytes, i.SpeedBps,
        i.Progress, i.Status.ToString(),
        i.SpeedFormatted, i.EtaFormatted,
        i.TotalFormatted, i.DownloadedFormatted,
        i.ErrorMessage, i.IsActive,
        i.ProgressVisualizationMode,
        i.ProgressVisualizationStage,
        i.GetThreadProgressSnapshot());
}