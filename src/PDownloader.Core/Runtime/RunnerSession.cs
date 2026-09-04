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

/// <summary>
/// Core-owned context for one Runner process. Sensitive headers and media
/// format selection never leave Core; Runner receives only UI-safe session data.
/// </summary>
public sealed class RunnerDownloadContext
{
    public string Url { get; init; } = string.Empty;
    public string FormatId { get; init; } = string.Empty;
    public string SaveTo { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public bool IsRunner { get; init; }
    public int Threads { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public List<DownloadCategoryDto> Categories { get; init; } = [];
    public string SelectedCategoryId { get; init; } = string.Empty;

    public RunnerSessionView ToView() => new()
    {
        Url = Url,
        SaveTo = SaveTo,
        FileName = FileName,
        Threads = Threads,
        IsRunner = IsRunner,
        Categories = Categories.Select(category => new DownloadCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            FolderPath = category.FolderPath,
            Extensions = [.. category.Extensions],
            IsEnabled = category.IsEnabled
        }).ToList(),
        SelectedCategoryId = SelectedCategoryId
    };
}

public sealed class RunnerSession
{
    public RunnerSession(
        string id,
        ConfluxService channel,
        RunnerDownloadContext context)
    {
        Id = id;
        Channel = channel;
        Context = context;
    }

    public string Id { get; }
    public ConfluxService Channel { get; }
    public RunnerDownloadContext Context { get; }
    public bool IsReady => Volatile.Read(ref _isReady);
    private bool _isReady;
    internal void MarkReady() => Volatile.Write(ref _isReady, true);
    internal CancellationTokenSource Lifetime { get; } = new();
    internal Task<ConfluxService> StartupTask { get; set; } = null!;
    internal Task? CloseTask { get; set; }
}
