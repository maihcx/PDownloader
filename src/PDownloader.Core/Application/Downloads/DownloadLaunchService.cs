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

namespace PDownloader.Core.Application.Downloads;

/// <summary>
/// Owns workflows that create downloads and Runner sessions.
/// </summary>
public sealed class DownloadLaunchService
{
    private readonly DownloadConfigService _downloadConfig;
    private readonly RunnerSessionManager _runnerSessions;
    private readonly DownloadManager _downloads;
    private readonly UserDataStore _userDataStore;

    public DownloadLaunchService(
        DownloadConfigService downloadConfig,
        RunnerSessionManager runnerSessions,
        DownloadManager downloads,
        UserDataStore userDataStore)
    {
        _downloadConfig = downloadConfig;
        _runnerSessions = runnerSessions;
        _downloads = downloads;
        _userDataStore = userDataStore;
    }

    public async Task LaunchFromUrlAsync(
        StartDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return;
        }

        string id = Guid.NewGuid().ToString();
        string fileName = await DownloadEngine
            .GetRemoteFileNameAsync(request.Url)
            .ConfigureAwait(false)
            ?? "Unknown";

        cancellationToken.ThrowIfCancellationRequested();

        _runnerSessions.EnsureStarted(id, new RunnerDownloadTask
        {
            Id = id,
            FileName = fileName,
            Url = request.Url,
            SaveTo = Helpers.GetDefaultFolder(_userDataStore),
            Threads = request.Threads,
            Headers = request.Headers
        });
    }

    public void StartFromRunner(
        RunnerSession session,
        RunnerStartDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        RunnerDownloadContext context = session.Context;
        if (string.IsNullOrWhiteSpace(context.Url))
        {
            return;
        }

        int defaultThreads = _downloadConfig.DownloadConfigs.DefaultThreadCount;
        int threads = request.Threads > 0
            ? request.Threads
            : context.Threads > 0
                ? context.Threads
                : defaultThreads;

        if (threads <= 0)
        {
            threads = 8;
        }

        string saveTo = string.IsNullOrWhiteSpace(request.SaveTo)
            ? context.SaveTo
            : request.SaveTo;
        string fileName = string.IsNullOrWhiteSpace(request.FileName)
            ? context.FileName
            : request.FileName;

        Dictionary<string, string>? headers = context.Headers is null
            ? null
            : new Dictionary<string, string>(
                context.Headers,
                StringComparer.OrdinalIgnoreCase);

        string? formatId = string.IsNullOrWhiteSpace(context.FormatId)
            ? null
            : context.FormatId;

        _downloads.Enqueue(
            id: session.Id,
            url: context.Url,
            saveTo: saveTo ?? string.Empty,
            fileName: fileName ?? string.Empty,
            threads: threads,
            isYoutube: formatId is not null,
            formatId: formatId,
            customHeaders: headers,
            mergeMode: _downloadConfig.GetFileMergeMode());
    }
}
