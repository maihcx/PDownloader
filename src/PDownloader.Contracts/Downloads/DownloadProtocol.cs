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

namespace PDownloader.Contracts.Downloads;

/// <summary>Names used by the current CFS transport for the download protocol.</summary>
public static class DownloadProtocol
{
    public const string GetListCommand = "downloader-svc-getlist";
    public const string ListMessage = "muxt-get-downloader-list";
    public const string ProgressMessage = "muxt-download-progress";
    public const string DownloadByLinkCommand = "download-by-link";
    public const string RunnerStartDownloadCommand = "runner-start-download";
    public const string RunnerPauseCommand = "runner-pause";
    public const string RunnerResumeCommand = "runner-resume";
    public const string RunnerRetryCommand = "runner-retry";
    public const string RunnerCancelCommand = "runner-cancel";
    public const string RunnerClearCommand = "runner-clear";
    public const string RunnerPauseAllCommand = "runner-pause-all";
    public const string RunnerResumeAllCommand = "runner-resume-all";
    public const string RunnerRetryAllCommand = "runner-retry-all";

    // Runner lifecycle messages.
    public const string RunnerCancelExperienceMessage = "runner-cancel-exp";
    public const string RunnerUiClosedMessage = "runner-ui-closed";

    // Legacy Core -> Runner messages kept centralized until the CFS envelope is upgraded.
    public const string RunnerDownloadMessage = "download";
    public const string RunnerCancelMessage = "cancel";

    // Wire values for RunnerClearCommand.
    public const string ClearCompletedValue = "completed";
    public const string ClearAllValue = "all";
}
