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

namespace PDownloader.Services.HostServices;

/// <summary>
/// Main-App proxy for the updater owned and executed by PDownloader Core.
/// </summary>
public sealed class UpdateHostService : INotifyPropertyChanged
{
    private Action<UpdateReleaseInfo>? _onUpdateFound;

    private UpdateStatus _status = UpdateStatus.Idle;
    public UpdateStatus Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetField(ref _downloadProgress, value);
    }

    private UpdateReleaseInfo? _latestRelease;
    public UpdateReleaseInfo? LatestRelease
    {
        get => _latestRelease;
        private set => SetField(ref _latestRelease, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    private long _installerSize;
    public long InstallerSize
    {
        get => _installerSize;
        private set => SetField(ref _installerSize, value);
    }

    private bool _isAutoUpdateEnabled;
    public bool IsAutoUpdateEnabled
    {
        get => _isAutoUpdateEnabled;
        private set => SetField(ref _isAutoUpdateEnabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Handle(string name, string value)
    {
        if (name != UpdateProtocol.StateMessage)
        {
            return;
        }

        try
        {
            UpdateStateSnapshot? snapshot =
                JsonSerializer.Deserialize<UpdateStateSnapshot>(value);
            if (snapshot is null)
            {
                return;
            }

            LatestRelease = snapshot.LatestRelease;
            ErrorMessage = snapshot.ErrorMessage;
            InstallerSize = snapshot.InstallerSize;
            DownloadProgress = snapshot.DownloadProgress;
            IsAutoUpdateEnabled = snapshot.IsAutoUpdateEnabled;
            Status = snapshot.Status;

            if (snapshot.Status == UpdateStatus.UpdateAvailable
                && snapshot.LatestRelease is { } release
                && Interlocked.Exchange(ref _onUpdateFound, null) is { } callback)
            {
                callback(release);
            }
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Invalid update state from Core: {ex.Message}");
        }
    }

    public async Task CheckAsync(
        Action<UpdateReleaseInfo>? onUpdateFound = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _onUpdateFound = onUpdateFound;
        if (!await TrySendCommandAsync(
                UpdateProtocol.CheckWithoutTrayNotificationCommand,
                cancellationToken))
        {
            _onUpdateFound = null;
            SetCoreUnavailable();
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await TrySendCommandAsync(
                UpdateProtocol.DownloadCommand,
                cancellationToken))
        {
            SetCoreUnavailable();
        }
    }

    public async Task LaunchInstallerAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await TrySendCommandAsync(
                UpdateProtocol.InstallCommand,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "PDownloader Core is not available.");
        }
    }

    public Task CancelAsync(CancellationToken cancellationToken = default) =>
        TrySendCommandAsync(UpdateProtocol.CancelCommand, cancellationToken);

    public Task RequestStateAsync(CancellationToken cancellationToken = default) =>
        TrySendCommandAsync(UpdateProtocol.GetStateCommand, cancellationToken);

    public async Task SetAutoUpdateEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!await TrySendCommandAsync(
                UpdateProtocol.SetAutoUpdatePrefix
                + enabled.ToString().ToLowerInvariant(),
                cancellationToken))
        {
            SetCoreUnavailable();
        }
    }

    private static async Task<bool> TrySendCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        ConfluxService? coreService = ConfluxManager.cfsPDownloaderCore;
        return coreService is not null
            && await coreService.SendAsync(
                UpdateProtocol.CommandMessage,
                command,
                cancellationToken: cancellationToken);
    }

    private void SetCoreUnavailable()
    {
        ErrorMessage = "PDownloader Core is not available.";
        Status = UpdateStatus.Error;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
