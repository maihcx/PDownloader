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

using PDownloader.Core.Services.DownloadServices;
using PDownloader.Downloads.Torrents;

namespace PDownloader.Core.Application.Downloads;

/// <summary>Creates one TorrentShell and one aggregate download job per torrent.</summary>
public sealed class TorrentWorkflowService
{
    private readonly TorrentEngineService _torrentEngine;
    private readonly TorrentShellSessionManager _shellSessions;
    private readonly DownloadConfigService _downloadConfig;
    private readonly DownloadManager _downloads;
    private readonly DownloadProgressPublisher _progress;

    public TorrentWorkflowService(TorrentEngineService torrentEngine,
        TorrentShellSessionManager shellSessions, DownloadConfigService downloadConfig,
        DownloadManager downloads, DownloadProgressPublisher progress)
    {
        _torrentEngine = torrentEngine;
        _shellSessions = shellSessions;
        _downloadConfig = downloadConfig;
        _downloads = downloads;
        _progress = progress;
    }

    public async Task LaunchAsync(string source, string? requestedFolder, int threads,
        Dictionary<string, string>? headers, CancellationToken cancellationToken = default)
    {
        TorrentPreparation preparation = await _torrentEngine
            .PrepareAsync(source, headers, cancellationToken).ConfigureAwait(false);
        _torrentEngine.Register(preparation);

        string subfolder = DownloadPathUtilities.SanitizeFileName(preparation.Name);
        bool preserveRequested = !string.IsNullOrWhiteSpace(requestedFolder);
        DownloadCategorySelection selection = _downloadConfig.CreateRunnerSelection(
            preparation.Name, requestedFolder, preserveRequested, DownloadKind.Torrent,
            preserveRequested ? null : subfolder);
        string saveTo = preserveRequested
            ? EnsureDestinationSubfolder(selection.SaveTo, subfolder)
            : selection.SaveTo;

        string id = Guid.NewGuid().ToString("N");
        await _shellSessions.EnsureStartedAsync(id, new TorrentShellContext
        {
            Source = source,
            Name = preparation.Name,
            InfoHash = preparation.InfoHash,
            TotalBytes = preparation.TotalBytes,
            SaveTo = saveTo,
            DestinationSubfolder = subfolder,
            Headers = CloneHeaders(headers),
            Categories = selection.Categories,
            SelectedCategoryId = selection.SelectedCategoryId,
            Files = preparation.Files.Select(file => new TorrentFileProgressDto
            {
                Index = file.Index,
                RelativePath = file.RelativePath,
                FileName = file.FileName,
                Length = file.Length,
                Status = DownloadStatus.Queued
            }).ToList()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(TorrentShellSession session, TorrentShellStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        if (!session.TryStart())
        {
            return;
        }

        try
        {
            TorrentShellContext context = session.Context;
            int[] selectedIndexes = request.SelectedFileIndexes.Distinct()
                .Where(index => context.Files.Any(file => file.Index == index)).ToArray();
            if (selectedIndexes.Length == 0)
            {
                throw new InvalidDataException("At least one torrent file must be selected.");
            }

            string saveTo = string.IsNullOrWhiteSpace(request.SaveTo) ? context.SaveTo : request.SaveTo;
            saveTo = EnsureDestinationSubfolder(saveTo, context.DestinationSubfolder);
            saveTo = _downloadConfig.PrepareOutputFolder(saveTo);
            if (request.RememberPathForCategory && !string.IsNullOrWhiteSpace(request.CategoryId))
            {
                _downloadConfig.RememberCategoryPath(request.CategoryId,
                    GetCategoryFolderToRemember(saveTo, context.DestinationSubfolder));
            }

            List<TorrentFileProgressDto> selectedFiles = context.Files
                .Where(file => selectedIndexes.Contains(file.Index))
                .Select(file => new TorrentFileProgressDto
                {
                    Index = file.Index,
                    RelativePath = file.RelativePath,
                    FileName = file.FileName,
                    SavePath = Path.Combine(saveTo,
                        file.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    Length = file.Length,
                    Status = DownloadStatus.Queued
                }).ToList();

            await _downloads.EnqueueAsync(id: session.Id, url: context.Source, saveTo: saveTo,
                fileName: context.Name, customHeaders: CloneHeaders(context.Headers),
                mergeMode: _downloadConfig.GetFileMergeMode(), downloadKind: DownloadKind.Torrent,
                torrentInfoHash: context.InfoHash, torrentFiles: selectedFiles,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _progress.AttachTorrentShell(session);
        }
        catch
        {
            session.ResetStart();
            throw;
        }
    }

    public TorrentShellContext CreateResumeContext(DownloadItem item)
    {
        string subfolder = Path.GetFileName(Path.TrimEndingDirectorySeparator(item.SavePath));
        DownloadCategorySelection selection = _downloadConfig.CreateRunnerSelection(
            item.FileName, item.SavePath, true, DownloadKind.Torrent);
        return new TorrentShellContext
        {
            Source = item.Url,
            Name = item.FileName,
            InfoHash = item.TorrentInfoHash,
            TotalBytes = item.TotalBytes,
            SaveTo = item.SavePath,
            DestinationSubfolder = subfolder,
            IsStarted = true,
            Headers = CloneHeaders(item.CustomHeaders),
            Categories = selection.Categories,
            SelectedCategoryId = selection.SelectedCategoryId,
            Files = item.GetTorrentFilesSnapshot().ToList()
        };
    }

    private static string EnsureDestinationSubfolder(string saveTo, string subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder)
            || string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(saveTo)),
                subfolder, StringComparison.OrdinalIgnoreCase))
        {
            return saveTo;
        }

        return Path.Combine(saveTo, subfolder);
    }

    private static string GetCategoryFolderToRemember(string saveTo, string subfolder) =>
        !string.IsNullOrWhiteSpace(subfolder)
        && string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(saveTo)), subfolder,
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(saveTo)) ?? saveTo
            : saveTo;

    private static Dictionary<string, string>? CloneHeaders(Dictionary<string, string>? headers) =>
        headers is null ? null : new(headers, StringComparer.OrdinalIgnoreCase);
}
