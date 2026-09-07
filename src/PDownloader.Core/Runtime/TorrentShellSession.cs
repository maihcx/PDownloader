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

namespace PDownloader.Core.Runtime;

public sealed class TorrentShellContext
{
    public required string Source { get; init; }
    public required string Name { get; init; }
    public required string InfoHash { get; init; }
    public required string SaveTo { get; init; }
    public required string DestinationSubfolder { get; init; }
    public long TotalBytes { get; init; }
    public bool IsStarted { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public List<DownloadCategoryDto> Categories { get; init; } = [];
    public string SelectedCategoryId { get; init; } = string.Empty;
    public List<TorrentFileProgressDto> Files { get; init; } = [];

    public TorrentShellSessionView ToView(string downloadId) => new()
    {
        DownloadId = downloadId,
        Name = Name,
        InfoHash = InfoHash,
        TotalBytes = TotalBytes,
        SaveTo = SaveTo,
        DestinationSubfolder = DestinationSubfolder,
        IsStarted = IsStarted,
        Categories = Categories.Select(CloneCategory).ToList(),
        SelectedCategoryId = SelectedCategoryId,
        Files = Files.Select(CloneFile).ToList()
    };

    private static DownloadCategoryDto CloneCategory(DownloadCategoryDto category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        FolderPath = category.FolderPath,
        Extensions = [.. category.Extensions],
        IsEnabled = category.IsEnabled
    };

    private static TorrentFileProgressDto CloneFile(TorrentFileProgressDto file) => new()
    {
        Index = file.Index,
        RelativePath = file.RelativePath,
        FileName = file.FileName,
        SavePath = file.SavePath,
        Length = file.Length,
        DownloadedBytes = file.DownloadedBytes,
        SpeedBps = file.SpeedBps,
        Progress = file.Progress,
        Status = file.Status,
        ErrorMessage = file.ErrorMessage
    };
}

public sealed class TorrentShellSession
{
    private int _started;
    private bool _isReady;

    public TorrentShellSession(string id, ConfluxService channel, TorrentShellContext context)
    {
        Id = id;
        Channel = channel;
        Context = context;
        _started = context.IsStarted ? 1 : 0;
    }

    public string Id { get; }
    public ConfluxService Channel { get; }
    public TorrentShellContext Context { get; }
    public bool IsReady => Volatile.Read(ref _isReady);
    public bool IsStarted => Volatile.Read(ref _started) != 0;
    public bool TryStart() => Interlocked.CompareExchange(ref _started, 1, 0) == 0;
    internal void ResetStart() => Volatile.Write(ref _started, 0);
    internal void MarkReady() => Volatile.Write(ref _isReady, true);
    internal CancellationTokenSource Lifetime { get; } = new();
    internal Task<ConfluxService> StartupTask { get; set; } = null!;
    internal Task? CloseTask { get; set; }
}
