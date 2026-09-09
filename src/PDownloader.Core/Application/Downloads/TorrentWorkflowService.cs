// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
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
    private readonly Lazy<TorrentEngineService> _torrentEngine;
    private readonly TorrentShellSessionManager _shellSessions;
    private readonly DownloadConfigService _downloadConfig;
    private readonly DownloadManager _downloads;
    private readonly DownloadProgressPublisher _progressPublisher;
    private readonly UserDataStore _userDataStore;

    public TorrentWorkflowService(
        Lazy<TorrentEngineService> torrentEngine,
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
        string requestedSaveTo = ResolveDownloadFolder(requestedFolder);
        DownloadCategorySelection selection = _downloadConfig.CreateRunnerSelection(
            fileName: null,
            requestedPath: requestedSaveTo,
            preserveRequestedPath: false,
            downloadKind: DownloadKind.Torrent);
        int actualThreads = threads > 0
            ? threads
            : _downloadConfig.DownloadConfigs.DefaultThreadCount;

        // Every torrent, including a single-file torrent, belongs to TorrentShell.
        // Runner remains dedicated to non-torrent, single-download experiences.
        string token = Guid.NewGuid().ToString("N");
        var context = new TorrentShellContext
        {
            Source = source,
            Categories = selection.Categories,
            SelectedCategoryId = selection.SelectedCategoryId,
            Threads = actualThreads,
            Headers = CloneHeaders(headers)
        };
        context.InitializeDestination(selection.SaveTo);

        TorrentShellSession session = await _shellSessions
            .StartAsync(token, context, cancellationToken)
            .ConfigureAwait(false);

        // Metadata discovery is intentionally detached from LaunchAsync after the
        // shell is ready, so the user sees TorrentShell immediately.
        _ = PrepareAndPublishAsync(session);
    }

    public async Task<TorrentShellStartResult> StartDownloadsAsync(
        TorrentShellSession session,
        TorrentShellStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        TorrentShellContext context = session.Context;
        TorrentPreparation? preparation = context.Preparation;
        if (preparation is null)
        {
            return new TorrentShellStartResult
            {
                Success = false,
                Error = string.IsNullOrWhiteSpace(context.MetadataError)
                    ? "Torrent metadata is still loading."
                    : context.MetadataError
            };
        }

        int[] selected = request.SelectedFileIndexes
            .Distinct()
            .Where(index => preparation.Files.Any(file => file.Index == index))
            .ToArray();

        if (selected.Length == 0)
        {
            return new TorrentShellStartResult
            {
                Success = false,
                Error = "Select at least one torrent file."
            };
        }

        string saveTo;
        try
        {
            string requestedSaveTo = string.IsNullOrWhiteSpace(request.SaveTo)
                ? context.SaveTo
                : request.SaveTo;
            saveTo = EnsureDestinationSubfolder(
                requestedSaveTo,
                context.DestinationSubfolder);
            saveTo = _downloadConfig.PrepareOutputFolder(saveTo);
            if (request.RememberPathForCategory
                && !string.IsNullOrWhiteSpace(request.CategoryId))
            {
                string categoryFolder = GetCategoryFolderToRemember(
                    saveTo,
                    context.DestinationSubfolder);
                _downloadConfig.RememberCategoryPath(request.CategoryId, categoryFolder);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TorrentShellStartResult
            {
                Success = false,
                Error = ex.Message
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
            _torrentEngine.Value.Register(preparation);

            string[] downloadIds = selected
                .Select(index => TorrentShellContext.CreateDownloadId(session.Id, index))
                .ToArray();
            session.SetDownloadIds(downloadIds);
            _progressPublisher.AttachTorrentShell(session);

            foreach (int index in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TorrentPreparedFile file = preparation.Files.First(candidate =>
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
                    torrentInfoHash: preparation.InfoHash,
                    torrentName: preparation.Name,
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

    private async Task PrepareAndPublishAsync(TorrentShellSession session)
    {
        TorrentShellContext context = session.Context;
        try
        {
            TorrentPreparation preparation = await _torrentEngine.Value
                .PrepareAsync(
                    context.Source,
                    CloneHeaders(context.Headers),
                    session.LifetimeToken)
                .ConfigureAwait(false);
            _torrentEngine.Value.Register(preparation);

            string destinationSubfolder = DownloadPathUtilities.SanitizeFileName(
                preparation.Name);
            string saveTo = EnsureDestinationSubfolder(
                context.SaveTo,
                destinationSubfolder);
            context.CompletePreparation(
                preparation,
                saveTo,
                destinationSubfolder);
        }
        catch (OperationCanceledException) when (session.LifetimeToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TorrentShell] Could not load torrent metadata: {ex}");
            context.FailPreparation(ex.Message);
        }

        try
        {
            if (!session.LifetimeToken.IsCancellationRequested)
            {
                session.Channel.Send(
                    DownloadProtocol.TorrentShellSessionChanged,
                    session.ToView());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TorrentShell] Could not publish torrent metadata: {ex.Message}");
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

    private static string EnsureDestinationSubfolder(
        string saveTo,
        string destinationSubfolder)
    {
        if (string.IsNullOrWhiteSpace(destinationSubfolder)
            || string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(saveTo)),
                destinationSubfolder,
                StringComparison.OrdinalIgnoreCase))
        {
            return saveTo;
        }

        return Path.Combine(saveTo, destinationSubfolder);
    }

    private static string GetCategoryFolderToRemember(
        string saveTo,
        string destinationSubfolder)
    {
        if (string.IsNullOrWhiteSpace(destinationSubfolder)
            || !string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(saveTo)),
                destinationSubfolder,
                StringComparison.OrdinalIgnoreCase))
        {
            return saveTo;
        }

        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(saveTo))
            ?? saveTo;
    }

    private static Dictionary<string, string>? CloneHeaders(
        Dictionary<string, string>? headers) =>
        headers is null
            ? null
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
}
