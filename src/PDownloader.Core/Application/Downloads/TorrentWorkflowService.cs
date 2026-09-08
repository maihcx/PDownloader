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

public sealed class TorrentWorkflowService
{
    private readonly TorrentEngineService _torrentEngine;
    private readonly TorrentShellSessionManager _shellSessions;
    private readonly DownloadConfigService _downloadConfig;
    private readonly DownloadManager _downloads;
    private readonly DownloadProgressPublisher _progressPublisher;
    private readonly UserDataStore _userDataStore;

    public TorrentWorkflowService(
        TorrentEngineService torrentEngine,
        TorrentShellSessionManager shellSessions,
        DownloadConfigService downloadConfig,
        DownloadManager downloads,
        DownloadProgressPublisher progressPublisher,
        UserDataStore userDataStore)
    {
        _torrentEngine = torrentEngine;
        _shellSessions = shellSessions;
        _downloadConfig = downloadConfig;
        _downloads = downloads;
        _progressPublisher = progressPublisher;
        _userDataStore = userDataStore;
    }

    public async Task LaunchAsync(
        string source,
        string? requestedFolder,
        int threads,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken = default)
    {
        TorrentPreparation preparation = await _torrentEngine
            .PrepareAsync(source, headers, cancellationToken)
            .ConfigureAwait(false);
        _torrentEngine.Register(preparation);

        string saveTo = ResolveDownloadFolder(requestedFolder);
        int actualThreads = threads > 0
            ? threads
            : _downloadConfig.DownloadConfigs.DefaultThreadCount;

        // Every torrent, including a single-file torrent, belongs to TorrentShell.
        // Runner remains dedicated to non-torrent, single-download experiences.
        string token = Guid.NewGuid().ToString("N");
        await _shellSessions.StartAsync(token, new TorrentShellContext
        {
            Source = source,
            SaveTo = saveTo,
            Threads = actualThreads,
            Headers = CloneHeaders(headers),
            Preparation = preparation
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TorrentShellStartResult> StartDownloadsAsync(
        TorrentShellSession session,
        TorrentShellStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        TorrentShellContext context = session.Context;
        int[] selected = request.SelectedFileIndexes
            .Distinct()
            .Where(index => context.Preparation.Files.Any(file => file.Index == index))
            .ToArray();

        if (selected.Length == 0)
        {
            return new TorrentShellStartResult
            {
                Success = false,
                Error = "Select at least one torrent file."
            };
        }

        if (!session.TryStart())
        {
            return new TorrentShellStartResult
            {
                Success = session.HasStarted,
                Error = session.HasStarted ? string.Empty : "The torrent session could not be started."
            };
        }

        try
        {
            _torrentEngine.Register(context.Preparation);
            string destinationSubfolder = DownloadPathUtilities.SanitizeFileName(
                context.Preparation.Name);
            DownloadCategorySelection selection = _downloadConfig.CreateRunnerSelection(
                context.Preparation.Name,
                context.SaveTo,
                preserveRequestedPath: false,
                downloadKind: DownloadKind.Torrent,
                destinationSubfolder: destinationSubfolder);
            string saveTo = _downloadConfig.PrepareOutputFolder(selection.SaveTo);

            string[] downloadIds = selected
                .Select(index => TorrentShellContext.CreateDownloadId(session.Id, index))
                .ToArray();
            session.SetDownloadIds(downloadIds);
            _progressPublisher.AttachTorrentShell(session);

            foreach (int index in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TorrentPreparedFile file = context.Preparation.Files.First(candidate =>
                    candidate.Index == index);
                await _downloads.EnqueueAsync(
                    id: TorrentShellContext.CreateDownloadId(session.Id, file.Index),
                    url: context.Source,
                    saveTo: saveTo,
                    fileName: string.IsNullOrWhiteSpace(file.FileName) ? "download" : file.FileName,
                    threads: context.Threads > 0 ? context.Threads : 8,
                    customHeaders: CloneHeaders(context.Headers),
                    mergeMode: _downloadConfig.GetFileMergeMode(),
                    downloadKind: DownloadKind.Torrent,
                    torrentInfoHash: context.Preparation.InfoHash,
                    torrentName: context.Preparation.Name,
                    torrentFileIndex: file.Index,
                    torrentRelativePath: file.RelativePath,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return new TorrentShellStartResult { Success = true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[TorrentShell] Could not start torrent downloads: {ex}");
            return new TorrentShellStartResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private string ResolveDownloadFolder(string? requestedFolder)
    {
        if (!string.IsNullOrWhiteSpace(requestedFolder))
        {
            return _downloadConfig.PrepareOutputFolder(requestedFolder);
        }

        string configured = _downloadConfig.DownloadConfigs.DefaultDownloadFolder;
        return _downloadConfig.PrepareOutputFolder(
            string.IsNullOrWhiteSpace(configured)
                ? Helpers.GetDefaultFolder(_userDataStore)
                : configured);
    }

    private static Dictionary<string, string>? CloneHeaders(
        Dictionary<string, string>? headers) =>
        headers is null
            ? null
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
}
