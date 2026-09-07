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

public sealed class TorrentSelectionContext
{
    public required string Source { get; init; }
    public required string SaveTo { get; init; }
    public required TorrentPreparation Preparation { get; init; }
    public int Threads { get; init; }
    public Dictionary<string, string>? Headers { get; init; }

    public TorrentSelectionSessionView ToView() => new()
    {
        Name = Preparation.Name,
        InfoHash = Preparation.InfoHash,
        TotalBytes = Preparation.TotalBytes,
        Files = Preparation.Files.Select(file => new TorrentSelectionFileDto
        {
            Index = file.Index,
            RelativePath = file.RelativePath,
            FileName = file.FileName,
            Length = file.Length
        }).ToList()
    };
}

public sealed class TorrentSelectionSession
{
    private int _completed;

    public TorrentSelectionSession(
        string id,
        ConfluxService channel,
        TorrentSelectionContext context)
    {
        Id = id;
        Channel = channel;
        Context = context;
    }

    public string Id { get; }
    public ConfluxService Channel { get; }
    public TorrentSelectionContext Context { get; }
    public bool TryComplete() => Interlocked.Exchange(ref _completed, 1) == 0;
    internal CancellationTokenSource Lifetime { get; } = new();
    internal Task<ConfluxService> StartupTask { get; set; } = null!;
    internal Task? CloseTask { get; set; }
}
