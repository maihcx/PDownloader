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

using Microsoft.Extensions.Hosting;

namespace PDownloader.Core.Update;

public sealed class CoreUpdateCoordinator : IDisposable
{
    private const string AutoUpdateSettingKey = "IsAutoUpdateEnabled";
    private static readonly TimeSpan BroadcastTimeout =
        TimeSpan.FromMilliseconds(250);

    private readonly CoreUpdateService _updateService;
    private readonly IHostApplicationLifetime _lifetime;
    private int _operationActive;
    private int _disposed;
    private CancellationTokenSource? _operationCancellation;
    private UpdateStatus _status = UpdateStatus.Idle;
    private double _downloadProgress;
    private double _lastBroadcastProgress;

    public CoreUpdateCoordinator(
        CoreUpdateService updateService,
        IHostApplicationLifetime lifetime)
    {
        _updateService = updateService;
        _lifetime = lifetime;
    }

    public bool IsAutoUpdateEnabled { get; private set; } =
        UserDataStore.GetValue<bool>(AutoUpdateSettingKey, true);

    public void HandleCommand(string command)
    {
        switch (command)
        {
            case UpdateProtocol.GetStateCommand:
                BroadcastState();
                break;

            case UpdateProtocol.CheckCommand:
                _ = CheckAsync(shouldNotifyTray: true);
                break;

            case UpdateProtocol.CheckWithoutTrayNotificationCommand:
                _ = CheckAsync(shouldNotifyTray: false);
                break;

            case UpdateProtocol.DownloadCommand:
                _ = DownloadAsync();
                break;

            case UpdateProtocol.InstallCommand:
                TryInstallReadyUpdate();
                break;

            case UpdateProtocol.CancelCommand:
                Cancel();
                break;

            default:
                if (command.StartsWith(
                        UpdateProtocol.SetAutoUpdatePrefix,
                        StringComparison.Ordinal)
                    && bool.TryParse(
                        command[UpdateProtocol.SetAutoUpdatePrefix.Length..],
                        out bool enabled))
                {
                    SetAutoUpdateEnabled(enabled);
                }

                break;
        }
    }

    public bool TryInstallPendingUpdateAtCoreStartup()
    {
        if (!_updateService.TryLaunchPendingInstaller())
        {
            if (!string.IsNullOrWhiteSpace(_updateService.ErrorMessage))
            {
                _status = UpdateStatus.Error;
            }

            return false;
        }

        _lifetime.StopApplication();
        return true;
    }

    public async Task RunAutomaticUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAutoUpdateEnabled
            || !TryEnterOperation())
        {
            return;
        }

        try
        {
            if (_status is UpdateStatus.Checking or UpdateStatus.Downloading)
            {
                return;
            }

            if (_status != UpdateStatus.UpdateAvailable
                && _status != UpdateStatus.ReadyToInstall)
            {
                await CheckCoreAsync(
                    cancellationToken,
                    shouldNotifyTray: true);
            }

            await CompleteAutomaticUpdateIfEnabledAsync(cancellationToken);
        }
        finally
        {
            ExitOperation();
        }
    }

    public void BroadcastState(bool shouldNotifyTray = false)
    {
        string json = JsonSerializer.Serialize(
            CreateSnapshot(shouldNotifyTray));
        AppRuntime.cfsMain?.Send(
            UpdateProtocol.StateMessage,
            json,
            BroadcastTimeout);
        AppRuntime.cfsTray?.Send(
            UpdateProtocol.StateMessage,
            json,
            BroadcastTimeout);
    }

    private async Task CheckAsync(bool shouldNotifyTray)
    {
        if (!TryEnterOperation())
        {
            return;
        }

        try
        {
            await CheckCoreAsync(
                CancellationToken.None,
                shouldNotifyTray);
            await CompleteAutomaticUpdateIfEnabledAsync(
                CancellationToken.None);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task DownloadAsync()
    {
        if (!TryEnterOperation())
        {
            return;
        }

        try
        {
            await DownloadCoreAsync(CancellationToken.None);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task CheckCoreAsync(
        CancellationToken cancellationToken,
        bool shouldNotifyTray)
    {
        if (_status is UpdateStatus.Checking or UpdateStatus.Downloading)
        {
            return;
        }

        CancellationToken operationToken =
            ResetOperationCancellation(cancellationToken).Token;
        SetStatus(UpdateStatus.Checking);

        try
        {
            bool hasUpdate = await _updateService.CheckForUpdateAsync(
                operationToken);
            if (hasUpdate)
            {
                SetStatus(
                    UpdateStatus.UpdateAvailable,
                    shouldNotifyTray);
            }
            else
            {
                SetStatus(UpdateStatus.UpToDate);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(UpdateStatus.Idle);
        }
        catch
        {
            SetStatus(UpdateStatus.Error);
        }
    }

    private async Task DownloadCoreAsync(CancellationToken cancellationToken)
    {
        if (_status != UpdateStatus.UpdateAvailable)
        {
            BroadcastState();
            return;
        }

        CancellationToken operationToken =
            ResetOperationCancellation(cancellationToken).Token;
        _downloadProgress = 0;
        _lastBroadcastProgress = 0;
        SetStatus(UpdateStatus.Downloading);

        var progress = new InlineProgress<double>(value =>
        {
            _downloadProgress = value;
            if (value >= 1 || value - _lastBroadcastProgress >= 0.01)
            {
                _lastBroadcastProgress = value;
                BroadcastState();
            }
        });

        try
        {
            await _updateService.DownloadInstallerAsync(
                progress,
                operationToken);
            _downloadProgress = 1;
            SetStatus(UpdateStatus.ReadyToInstall);
        }
        catch (OperationCanceledException)
        {
            _downloadProgress = 0;
            SetStatus(UpdateStatus.UpdateAvailable);
        }
        catch
        {
            SetStatus(UpdateStatus.Error);
        }
    }

    private void TryInstallReadyUpdate()
    {
        if (_status != UpdateStatus.ReadyToInstall
            || !TryEnterOperation())
        {
            return;
        }

        try
        {
            InstallCore();
        }
        catch
        {
            SetStatus(UpdateStatus.Error);
        }
        finally
        {
            ExitOperation();
        }
    }

    private void InstallCore()
    {
        _updateService.LaunchInstaller();
        _lifetime.StopApplication();
    }

    private async Task CompleteAutomaticUpdateIfEnabledAsync(
        CancellationToken cancellationToken)
    {
        if (!IsAutoUpdateEnabled)
        {
            return;
        }

        if (_status == UpdateStatus.UpdateAvailable)
        {
            await DownloadCoreAsync(cancellationToken);
        }

        if (_status == UpdateStatus.ReadyToInstall)
        {
            try
            {
                InstallCore();
            }
            catch
            {
                SetStatus(UpdateStatus.Error);
            }
        }
    }

    private void SetAutoUpdateEnabled(bool enabled)
    {
        IsAutoUpdateEnabled = enabled;
        // Merge settings written by Main/Tray before Core persists its value.
        UserDataStore.Reload();
        UserDataStore.SetValue(AutoUpdateSettingKey, enabled);
        BroadcastState();

        if (enabled)
        {
            _ = RunAutomaticUpdateAsync();
        }
    }

    private void SetStatus(
        UpdateStatus status,
        bool shouldNotifyTray = false)
    {
        _status = status;
        BroadcastState(shouldNotifyTray);
    }

    private UpdateStateSnapshot CreateSnapshot(bool shouldNotifyTray) => new()
    {
        Status = _status,
        DownloadProgress = _downloadProgress,
        LatestRelease = _updateService.CreateReleaseInfo(),
        ErrorMessage = _updateService.ErrorMessage,
        InstallerSize = _updateService.InstallerSize,
        IsAutoUpdateEnabled = IsAutoUpdateEnabled,
        ShouldNotifyTray = shouldNotifyTray,
    };

    private CancellationTokenSource ResetOperationCancellation(
        CancellationToken cancellationToken)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.ApplicationStopping);
        _operationCancellation = operationCancellation;
        return operationCancellation;
    }

    private bool TryEnterOperation() =>
        Interlocked.CompareExchange(ref _operationActive, 1, 0) == 0;

    private void ExitOperation() =>
        Volatile.Write(ref _operationActive, 0);

    private void Cancel()
    {
        _operationCancellation?.Cancel();
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value) => _handler(value);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        GC.SuppressFinalize(this);
    }

}
