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

public record DownloadItemSnapshot(
    string Id, string Url, string FileName, string SavePath,
    int Threads, bool IsYoutube, string? FormatId,
    long TotalBytes, long DownloadedBytes,
    string Status, string ErrorMessage,
    DateTime StartTime, DateTime EndTime)
{
    public string? ResolvedUrl { get; init; }

    public static DownloadItemSnapshot From(DownloadItem i) => new(
        i.Id, i.Url, i.FileName, i.SavePath,
        i.Threads, i.IsYoutube, i.FormatId,
        i.TotalBytes, i.DownloadedBytes,
        i.Status.ToString(), i.ErrorMessage,
        i.StartTime, i.EndTime)
    {
        ResolvedUrl = i.ResolvedUrl
    };

    public DownloadItem ToDownloadItem()
    {
        DownloadStatus status = Enum.TryParse<DownloadStatus>(Status, out DownloadStatus s) ? s : DownloadStatus.Queued;
        return new DownloadItem
        {
            Id = Id,
            Url = Url,
            ResolvedUrl = ResolvedUrl ?? string.Empty,
            FileName = FileName,
            SavePath = SavePath,
            Threads = Threads,
            IsYoutube = IsYoutube,
            FormatId = FormatId,
            TotalBytes = TotalBytes,
            DownloadedBytes = DownloadedBytes,
            Status = status,
            ErrorMessage = ErrorMessage,
            StartTime = StartTime,
            EndTime = EndTime
        };
    }
}
