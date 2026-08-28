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

using PDownloader.Contracts.Ipc;

namespace PDownloader.Contracts.Updates;

public static class UpdateProtocol
{
    public static readonly IpcRequestDefinition<IpcNoPayload, UpdateStateSnapshot> GetState =
        new("update.state.get");

    public static readonly IpcMessageDefinition<UpdateCommandRequest> Command =
        new("update.command");

    public static readonly IpcMessageDefinition<UpdateStateSnapshot> State =
        new("update.state");
}

public enum UpdateCommandKind
{
    Check,
    CheckWithoutTrayNotification,
    Download,
    Install,
    Cancel,
    SetAutoUpdate
}

public sealed record UpdateCommandRequest(
    UpdateCommandKind Command,
    bool? Enabled = null);

public enum UpdateStatus
{
    Idle,
    Checking,
    UpdateAvailable,
    Downloading,
    ReadyToInstall,
    UpToDate,
    Error,
}

public sealed class UpdateReleaseInfo
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
}

public sealed class UpdateStateSnapshot
{
    public UpdateStatus Status { get; set; }
    public double DownloadProgress { get; set; }
    public UpdateReleaseInfo? LatestRelease { get; set; }
    public string? ErrorMessage { get; set; }
    public long InstallerSize { get; set; }
    public bool IsAutoUpdateEnabled { get; set; }
    public bool ShouldNotifyTray { get; set; }
}
