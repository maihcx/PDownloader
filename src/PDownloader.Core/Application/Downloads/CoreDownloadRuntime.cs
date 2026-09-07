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
/// Core-owned implementation of the process capabilities consumed by the
/// download module. This replaces the old static DownloadRuntime hook.
/// </summary>
public sealed class CoreDownloadRuntime : IDownloadRuntime
{
    private readonly DownloadConfigService _downloadConfig;
    private readonly RunnerSessionManager _runnerSessions;
    private readonly TorrentShellSessionManager _torrentShellSessions;
    private readonly UserDataStore _userDataStore;

    public CoreDownloadRuntime(
        DownloadConfigService downloadConfig,
        RunnerSessionManager runnerSessions,
        TorrentShellSessionManager torrentShellSessions,
        UserDataStore userDataStore)
    {
        _downloadConfig = downloadConfig;
        _runnerSessions = runnerSessions;
        _torrentShellSessions = torrentShellSessions;
        _userDataStore = userDataStore;
    }

    public string? DefaultDownloadFolder =>
        _downloadConfig.DownloadConfigs.DefaultDownloadFolder;

    public string? DefaultTempFolder =>
        _downloadConfig.DownloadConfigs.DefaultTempFolder;

    public string FallbackDownloadFolder => Helpers.GetDefaultFolder(_userDataStore);

    public void ShowRunner(string id, RunnerDownloadTask task)
    {
        // IDownloadRuntime is synchronous. Do not block download control on UI startup.
        _ = ShowRunnerAsync(id, task);
    }

    private async Task ShowRunnerAsync(string id, RunnerDownloadTask task)
    {
        try { await _runnerSessions.EnsureStartedAsync(id, task).ConfigureAwait(false); }
        catch (Exception ex) { Debug.WriteLine($"[Runner] Could not show '{id}': {ex.Message}"); }
    }

    public void ShowTorrentShell(string id, RunnerDownloadTask task) =>
        _ = ShowTorrentShellAsync(id, task);

    private async Task ShowTorrentShellAsync(string id, RunnerDownloadTask task)
    {
        try
        {
            DownloadCategorySelection selection = _downloadConfig.CreateRunnerSelection(
                task.FileName, task.SaveTo, preserveRequestedPath: true, DownloadKind.Torrent);
            await _torrentShellSessions.EnsureStartedAsync(id, new TorrentShellContext
            {
                Source = task.Url,
                Name = task.FileName,
                InfoHash = task.TorrentInfoHash,
                TotalBytes = task.FileSize,
                SaveTo = task.SaveTo,
                DestinationSubfolder = Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(task.SaveTo)),
                IsStarted = true,
                Headers = task.Headers,
                Categories = selection.Categories,
                SelectedCategoryId = selection.SelectedCategoryId,
                Files = task.TorrentFiles
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TorrentShell] Could not show '{id}': {ex.Message}");
        }
    }
}
