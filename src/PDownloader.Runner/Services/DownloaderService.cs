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

namespace PDownloader.Runner.Services;

/// <summary>
/// Managed host of the application.
/// </summary>
public class DownloaderService : IHostedService, IDisposable
{
    private readonly RunnerConfig _runnerConfig;

    public ConfluxService? CfsContact;

    public DownloaderServiceStatus DownloaderStatus = new();

    public event Action<DownloadItemDto>? OnProgress;

    private DownloadStatus? _lastReceivedProgressStatus;
    private bool _disposed;

    public DownloaderService(RunnerConfig runnerConfig)
    {
        _runnerConfig = runnerConfig;
    }

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync(cancellationToken);
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (CfsContact is null)
        {
            return;
        }

        CfsContact.SetReady(false);
        try
        {
            await CfsContact.SendAsync(
                DownloaderStatus.State == RunnerState.Form
                    ? DownloadProtocol.RunnerCancelExperience : DownloadProtocol.RunnerUiClosed,
                TimeSpan.FromSeconds(1), cancellationToken);
        }
        finally { await CfsContact.StopServiceAsync(); }
    }

    public async Task<DownloaderServiceStatus> StartDownload()
    {
        if (DownloaderStatus == null)
        {
            DownloaderStatus = new DownloaderServiceStatus();
        }

        if (string.IsNullOrWhiteSpace(_runnerConfig.InitialUrl))
        {
            DownloaderStatus.ErrorKey = "err_download_uri_unavailable_title";
            DownloaderStatus.HasError = true;
        }
        else if (string.IsNullOrWhiteSpace(_runnerConfig.SaveTo) || !Directory.Exists(_runnerConfig.SaveTo))
        {
            DownloaderStatus.ErrorKey = "err_download_folder_not_exists_title";
            DownloaderStatus.HasError = true;
        }
        else
        {
            DownloaderStatus.HasError = false;
            DownloaderStatus.ErrorKey = string.Empty;
            DownloaderStatus.IsSending = true;

            // Save defaults
            //UserDataStore.SetValue("DefaultDownloadFolder", SaveTo);
            //UserDataStore.SetValue("DefaultThreads", Threads);

            var request = new RunnerStartDownloadRequest
            {
                SaveTo = _runnerConfig.SaveTo,
                FileName = _runnerConfig.FileName,
                Threads = _runnerConfig.Threads,
                CategoryId = _runnerConfig.SelectedCategory?.Id ?? string.Empty,
                RememberPathForCategory = _runnerConfig.RememberPathForCategory
            };

            bool ok = await Task.Run(() => SendWithRetry(request, retries: 3));

            DownloaderStatus.IsSending = false;

            if (ok)
            {
                DownloaderStatus.StatusKey = "stt_download_conneting_title";
                DownloaderStatus.State = RunnerState.Downloading;
            }
            else
            {
                DownloaderStatus.ErrorKey = "err_download_pdcore_notvalid_title";
                DownloaderStatus.HasError = true;
            }
        }

        return DownloaderStatus;
    }

    public void PauseDownload()
    {
        if (DownloaderStatus.IsPaused)
        {
            return;
        }

        CfsContact?.Send(DownloadProtocol.RunnerPause, new DownloadIdRequest(_runnerConfig.Token), TimeSpan.FromSeconds(30));
        //DownloaderStatus.IsPaused = true;
    }

    public void ResumeDownload()
    {
        if (!DownloaderStatus.IsPaused)
        {
            return;
        }

        CfsContact?.Send(DownloadProtocol.RunnerResume, new DownloadIdRequest(_runnerConfig.Token), TimeSpan.FromSeconds(30));
        //DownloaderStatus.IsPaused = false;
    }

    public void RetryDownload()
    {
        if (!DownloaderStatus.HasError)
        {
            return;
        }

        CfsContact?.Send(DownloadProtocol.RunnerRetry, new DownloadIdRequest(_runnerConfig.Token), TimeSpan.FromSeconds(30));
        //DownloaderStatus.IsPaused = false;
    }

    public void CancelDownload()
    {
        CfsContact?.Send(DownloadProtocol.RunnerCancel, new DownloadIdRequest(_runnerConfig.Token), TimeSpan.FromSeconds(30));
        DownloaderStatus.State = RunnerState.Form;
        //DownloaderStatus.IsPaused = false;
    }

    /// <summary>
    /// Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync(CancellationToken cancellationToken)
    {
        CfsContact = new ConfluxService();
        CfsContact.SetReady(false);
        CfsContact.Register(
            IpcTopology.CoreProcessName,
            IpcTopology.RunnerToCorePipeName(_runnerConfig.Token),
            IpcTopology.CoreToRunnerPipeName(_runnerConfig.Token)
        );
        CfsContact.RegisterMessageHandler(
            AppProtocol.State,
            RunnerCommandHandler.HandleState);
        CfsContact.RegisterMessageHandler(
            AppProtocol.MainEvent,
            RunnerCommandHandler.HandleMainEvent);
        CfsContact.RegisterMessageHandler(
            DownloadProtocol.Progress,
            HandleProgress);

        await CfsContact.StartServiceAsync();
        await CfsContact.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), cancellationToken);
        await LoadSessionAsync(cancellationToken);
    }

    private void HandleProgress(DownloadItemDto dto)
    {
        DownloadStatus status = dto.Status;
        if (_lastReceivedProgressStatus is DownloadStatus.Completed
            or DownloadStatus.Cancelled)
        {
            if (status != _lastReceivedProgressStatus.Value)
            {
                return;
            }
        }

        _lastReceivedProgressStatus = status;
        DownloaderStatus.IsPaused = status == DownloadStatus.Paused;
        DownloaderStatus.IsSending = status is DownloadStatus.Queued
            or DownloadStatus.Connecting;
        OnProgress?.Invoke(dto);
    }

    private async Task LoadSessionAsync(CancellationToken cancellationToken)
    {
        if (CfsContact is null || string.IsNullOrWhiteSpace(_runnerConfig.Token))
        {
            return;
        }

        const int retries = 5;
        for (int attempt = 1; attempt <= retries; attempt++)
        {
            IpcRequestResult<RunnerSessionView> result =
                await CfsContact.RequestAsync(
                    DownloadProtocol.RunnerGetSession,
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

            if (result.Success && result.Value is RunnerSessionView session)
            {
                _runnerConfig.ApplySession(session);
                return;
            }

            if (attempt < retries)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new IOException("Runner could not load its session from Core.");
    }

    private bool SendWithRetry(RunnerStartDownloadRequest request, int retries)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                bool ok = CfsContact?.Send(DownloadProtocol.RunnerStartDownload, request) ?? false;
                if (ok)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Runner] Send attempt {i + 1} failed: {ex.Message}");
            }

            if (i < retries - 1)
            {
                Thread.Sleep(500);
            }
        }

        return false;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            CfsContact?.Dispose();
            GC.SuppressFinalize(this);
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }
}
