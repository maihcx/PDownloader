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
    private readonly UserDataStore _userDataStore;

    public CoreDownloadRuntime(
        DownloadConfigService downloadConfig,
        RunnerSessionManager runnerSessions,
        UserDataStore userDataStore)
    {
        _downloadConfig = downloadConfig;
        _runnerSessions = runnerSessions;
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
}
