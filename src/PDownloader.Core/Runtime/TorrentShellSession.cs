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

using PDownloader.Downloads.Torrents;

namespace PDownloader.Core.Runtime;

public sealed class TorrentShellContext
{
    private readonly object _sync = new();
    private TorrentPreparation? _preparation;
    private string _saveTo = string.Empty;
    private string _destinationSubfolder = string.Empty;
    private string _metadataError = string.Empty;

    public required string Source { get; init; }
    public required IReadOnlyList<DownloadCategoryDto> Categories { get; init; }
    public required string SelectedCategoryId { get; init; }
    public int Threads { get; init; }
    public Dictionary<string, string>? Headers { get; init; }

    public TorrentPreparation? Preparation
    {
        get
        {
            lock (_sync)
            {
                return _preparation;
            }
        }
    }

    public string SaveTo
    {
        get
        {
            lock (_sync)
            {
                return _saveTo;
            }
        }
    }

    public string DestinationSubfolder
    {
        get
        {
            lock (_sync)
            {
                return _destinationSubfolder;
            }
        }
    }

    public string MetadataError
    {
        get
        {
            lock (_sync)
            {
                return _metadataError;
            }
        }
    }

    public bool IsLoading
    {
        get
        {
            lock (_sync)
            {
                return _preparation is null && string.IsNullOrWhiteSpace(_metadataError);
            }
        }
    }

    public void InitializeDestination(string saveTo)
    {
        lock (_sync)
        {
            _saveTo = saveTo;
        }
    }

    public void CompletePreparation(
        TorrentPreparation preparation,
        string saveTo,
        string destinationSubfolder)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        lock (_sync)
        {
            _preparation = preparation;
            _saveTo = saveTo;
            _destinationSubfolder = destinationSubfolder;
            _metadataError = string.Empty;
        }
    }

    public void FailPreparation(string error)
    {
        lock (_sync)
        {
            _metadataError = error;
        }
    }

    public TorrentShellSessionView ToView(string sessionId)
    {
        lock (_sync)
        {
            TorrentPreparation? preparation = _preparation;
            return new TorrentShellSessionView
            {
                Name = preparation?.Name ?? string.Empty,
                InfoHash = preparation?.InfoHash ?? string.Empty,
                SaveTo = _saveTo,
                DestinationSubfolder = _destinationSubfolder,
                IsLoading = preparation is null && string.IsNullOrWhiteSpace(_metadataError),
                MetadataError = _metadataError,
                TotalBytes = preparation?.TotalBytes ?? 0,
                Categories = Categories.Select(CloneCategory).ToList(),
                SelectedCategoryId = SelectedCategoryId,
                Files = preparation?.Files.Select(file => new TorrentShellFileDto
                {
                    Index = file.Index,
                    DownloadId = CreateDownloadId(sessionId, file.Index),
                    RelativePath = file.RelativePath,
                    FileName = file.FileName,
                    Length = file.Length
                }).ToList() ?? []
            };
        }
    }

    private static DownloadCategoryDto CloneCategory(DownloadCategoryDto category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        FolderPath = category.FolderPath,
        IsEnabled = category.IsEnabled,
        Extensions = [.. category.Extensions]
    };

    public static string CreateDownloadId(string sessionId, int fileIndex) =>
        $"{sessionId}-{fileIndex:D5}";
}

public sealed class TorrentShellSession
{
    private readonly object _downloadIdsLock = new();
    private readonly HashSet<string> _downloadIds = new(StringComparer.Ordinal);
    private int _started;
    private int _ready;

    public TorrentShellSession(
        string id,
        ConfluxService channel,
        TorrentShellContext context)
    {
        Id = id;
        Channel = channel;
        Context = context;
    }

    public string Id { get; }
    public ConfluxService Channel { get; }
    public TorrentShellContext Context { get; }
    public bool IsReady => Volatile.Read(ref _ready) != 0;
    public bool HasStarted => Volatile.Read(ref _started) != 0;
    public CancellationToken LifetimeToken => Lifetime.Token;
    public bool TryStart() => Interlocked.CompareExchange(ref _started, 1, 0) == 0;
    public void MarkReady() => Volatile.Write(ref _ready, 1);
    public TorrentShellSessionView ToView() => Context.ToView(Id);

    public void SetDownloadIds(IEnumerable<string> downloadIds)
    {
        lock (_downloadIdsLock)
        {
            _downloadIds.Clear();
            foreach (string id in downloadIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                _downloadIds.Add(id);
            }
        }
    }

    public bool OwnsDownload(string downloadId)
    {
        lock (_downloadIdsLock)
        {
            return _downloadIds.Contains(downloadId);
        }
    }

    public string[] GetDownloadIds()
    {
        lock (_downloadIdsLock)
        {
            return _downloadIds.ToArray();
        }
    }

    internal CancellationTokenSource Lifetime { get; } = new();
    internal Task<ConfluxService> StartupTask { get; set; } = null!;
    internal Task? CloseTask { get; set; }
}
