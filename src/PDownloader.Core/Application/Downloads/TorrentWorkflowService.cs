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
    private readonly TorrentSelectionSessionManager _selectionSessions;
    private readonly RunnerSessionManager _runnerSessions;
    private readonly DownloadConfigService _downloadConfig;
    private readonly UserDataStore _userDataStore;

    public TorrentWorkflowService(
        TorrentEngineService torrentEngine,
        TorrentSelectionSessionManager selectionSessions,
        RunnerSessionManager runnerSessions,
        DownloadConfigService downloadConfig,
        UserDataStore userDataStore)
    {
        _torrentEngine = torrentEngine;
        _selectionSessions = selectionSessions;
        _runnerSessions = runnerSessions;
        _downloadConfig = downloadConfig;
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

        if (preparation.Files.Count == 1)
        {
            await LaunchRunnerAsync(
                source,
                saveTo,
                actualThreads,
                headers,
                preparation,
                preparation.Files[0],
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string token = Guid.NewGuid().ToString("N");
        await _selectionSessions.StartAsync(token, new TorrentSelectionContext
        {
            Source = source,
            SaveTo = saveTo,
            Threads = actualThreads,
            Headers = CloneHeaders(headers),
            Preparation = preparation
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteSelectionAsync(
        TorrentSelectionSession session,
        TorrentSelectionResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        if (!session.TryComplete())
        {
            return;
        }

        try
        {
            TorrentSelectionContext context = session.Context;
            int[] selected = result.SelectedFileIndexes
                .Distinct()
                .Where(index => context.Preparation.Files.Any(file => file.Index == index))
                .ToArray();
            if (selected.Length == 0)
            {
                return;
            }

            _torrentEngine.Register(context.Preparation);
            await Task.WhenAll(selected.Select(index =>
            {
                TorrentPreparedFile file = context.Preparation.Files.First(candidate =>
                    candidate.Index == index);
                return LaunchRunnerAsync(
                    context.Source,
                    context.SaveTo,
                    context.Threads,
                    context.Headers,
                    context.Preparation,
                    file,
                    cancellationToken);
            })).ConfigureAwait(false);
        }
        finally
        {
            await _selectionSessions.CloseAsync(session.Id).ConfigureAwait(false);
        }
    }

    private Task LaunchRunnerAsync(
        string source,
        string saveTo,
        int threads,
        Dictionary<string, string>? headers,
        TorrentPreparation preparation,
        TorrentPreparedFile file,
        CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        return _runnerSessions.EnsureStartedAsync(id, new RunnerDownloadTask
        {
            Id = id,
            Url = source,
            SaveTo = saveTo,
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? "download" : file.FileName,
            Title = preparation.Name,
            FileSize = file.Length,
            Threads = threads,
            Headers = CloneHeaders(headers),
            DownloadKind = DownloadKind.Torrent,
            DestinationSubfolder = DownloadPathUtilities.SanitizeFileName(preparation.Name),
            TorrentInfoHash = preparation.InfoHash,
            TorrentFileIndex = file.Index,
            TorrentRelativePath = file.RelativePath
        }, cancellationToken);
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
