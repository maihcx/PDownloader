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

namespace PDownloader.Core.Runtime;

public static class CFSCommandHandler
{
    public record YoutubePendingMeta(string FormatId);

    private static readonly ConcurrentDictionary<string, YoutubePendingMeta> _youtubePending = new();
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _runnerPendingHeaders = new();
    private static readonly ConcurrentDictionary<string, object> _broadcastLocks = new();

    public static DownloadConfigService DownloadConfigService { get; set; } = Program.GetRequiredService<DownloadConfigService>();

    private static Action? mainAppAction { get; set; }

    public static void Handle(IpcReceivedMessage message)
    {
        if (message.TryGetPayload(AppProtocol.MainEvent, out MainAppEvent mainEvent))
        {
            AppRuntime.cfsTray?.Send(AppProtocol.MainEvent, mainEvent);
            foreach ((_, ConfluxService? service) in DownloadRunner.DownloaderCFSRest)
            {
                service.Send(AppProtocol.MainEvent, mainEvent);
            }

            return;
        }

        if (message.TryGetPayload(AppProtocol.TrayEvent, out TrayNavigationEvent trayEvent))
        {
            HandleMainEvent(AppProtocol.TrayEvent, trayEvent);
            return;
        }

        if (message.TryGetPayload(AppProtocol.State, out AppState state))
        {
            HandleMainEvent(AppProtocol.State, state);
            return;
        }

        if (message.TryGetPayload(AppProtocol.CoreServiceState, out AppState coreState))
        {
            HandleCoreState(coreState);
            return;
        }

        if (message.TryGetPayload(AppProtocol.CoreEventMessage, out CoreEvent coreEvent))
        {
            HandleCoreEvent(coreEvent);
            return;
        }

        if (message.TryGetPayload(UpdateProtocol.Command, out UpdateCommandRequest updateCommand))
        {
            Program.GetRequiredService<CoreUpdateCoordinator>()
                .HandleCommand(updateCommand);
            return;
        }

        if (message.TryGetPayload(DownloadProtocol.DownloadByLink, out StartDownloadRequest linkRequest))
        {
            _ = HandleDownloadByLink(linkRequest);
            return;
        }

        if (message.TryGetPayload(DownloadProtocol.RunnerStartDownload, out StartDownloadRequest startRequest))
        {
            HandleStartDownload(startRequest);
            return;
        }

        if (message.TryGetPayload(DownloadProtocol.RunnerResume, out DownloadIdRequest resumeRequest))
        {
            DownloadManager.Instance.Resume(resumeRequest.DownloadId);
            return;
        }

        if (message.TryGetPayload(DownloadProtocol.RunnerRetry, out DownloadIdRequest retryRequest))
        {
            DownloadManager.Instance.Retry(retryRequest.DownloadId);
            return;
        }

        if (message.TryGetPayload(DownloadProtocol.RunnerCancel, out DownloadIdRequest cancelRequest))
        {
            DownloadManager.Instance.Cancel(cancelRequest.DownloadId);
            return;
        }

        if (message.TryGetPayload(DownloadProtocol.RunnerPause, out DownloadIdRequest pauseRequest))
        {
            DownloadManager.Instance.Pause(pauseRequest.DownloadId);
            return;
        }

        if (message.TryGetPayload(DownloadProtocol.RunnerClear, out DownloadClearScope clearScope))
        {
            DownloadManager.Instance.ClearAll(clearScope);
            return;
        }

        if (message.Is(DownloadProtocol.RunnerPauseAll))
        {
            DownloadManager.Instance.PauseAll();
            return;
        }

        if (message.Is(DownloadProtocol.RunnerResumeAll))
        {
            DownloadManager.Instance.ResumeAll();
            return;
        }

        if (message.Is(DownloadProtocol.RunnerRetryAll))
        {
            DownloadManager.Instance.RetryAll();
        }
    }

    private static void HandleMainEvent<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        if (!AppRuntime.cfsMain!.IsAppStarted())
        {
            mainAppAction = () =>
            {
                _ = SendPendingMainEventAsync(definition, payload);
            };

            AppRuntime.cfsMain.StartApp();
        }
        else
        {
            _ = AppRuntime.cfsMain.SendAsync(definition, payload);
        }
    }

    private static async Task SendPendingMainEventAsync<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        ConfluxService? mainService = AppRuntime.cfsMain;
        if (mainService is not null
            && await mainService.SendAsync(definition, payload))
        {
            mainAppAction = null;
        }
    }

    private static void HandleCoreState(AppState state)
    {
        if (state == AppState.Shutdown)
        {
            if (AppRuntime.cfsMain!.IsAppStarted())
            {
                AppRuntime.cfsMain.Send(AppProtocol.State, state);
            }

            DownloadRunner.EnsureCloseAllRunnerStarted();
            AppRuntime.bootstrap?.Shutdown();
        }
    }

    private static void HandleCoreEvent(CoreEvent coreEvent)
    {
        switch (coreEvent)
        {
            case CoreEvent.RefreshDownloaderConfigs:
                DownloadConfigService.Reload();
                break;

            case CoreEvent.Ping:
                mainAppAction?.Invoke();
                break;
        }
    }

    private static async Task HandleDownloadByLink(StartDownloadRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
        {
            return;
        }

        var id = Guid.NewGuid().ToString();
        string fileName = await DownloadEngine.GetRemoteFileNameAsync(req.Url) ?? "Unknown";
        DownloadRunner.EnsureRunnerStarted(id, new()
        {
            Id = id,
            FileName = fileName,
            Url = req.Url,
            SaveTo = Helpers.GetDefaultFolder()
        });
    }

    private static void HandleStartDownload(StartDownloadRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
        {
            return;
        }

        _youtubePending.TryRemove(req.Id, out YoutubePendingMeta? ytMeta);

        Dictionary<string, string>? customHeaders = TakeRunnerPendingHeaders(req.Id)
            ?? NormalizeHeaders(req.Headers);

        int defaultThreads = DownloadConfigService.DownloadConfigs?.DefaultThreadCount ?? 0;
        FileMergeMode mergeMode = DownloadConfigService.GetFileMergeMode();

        DownloadManager.Instance.Enqueue(
            id: req.Id,
            url: req.Url,
            saveTo: req.SaveTo ?? string.Empty,
            fileName: req.FileName ?? string.Empty,
            threads: req.Threads > 0 ? req.Threads : defaultThreads,
            isYoutube: ytMeta != null,
            formatId: ytMeta?.FormatId,
            customHeaders: customHeaders,
            mergeMode: mergeMode);
    }

    public static void BroadcastItemChanged(DownloadItem item)
    {
        object broadcastLock = _broadcastLocks.GetOrAdd(item.Id, static _ => new object());

        lock (broadcastLock)
        {
            DownloadItemDto dto = DownloadManager.ToContract(item);
            DownloadRunner.DownloaderCFSRest.TryGetValue(
                item.Id,
                out ConfluxService? cfsDowloaderUI);

            AppRuntime.cfsMain?.Send(DownloadProtocol.Progress, dto);
            cfsDowloaderUI?.Send(DownloadProtocol.Progress, dto);
        }
    }

    public static void RegisterRunnerPendingHeaders(
        string id,
        Dictionary<string, string>? headers)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        Dictionary<string, string>? normalized = NormalizeHeaders(headers);
        if (normalized == null)
        {
            _runnerPendingHeaders.TryRemove(id, out _);
            return;
        }

        // Store an independent copy because the original RunnerDownloadTask may be reused or
        // mutated after the Runner has been launched.
        _runnerPendingHeaders[id] = normalized;
    }

    public static void ClearRunnerPendingContext(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _runnerPendingHeaders.TryRemove(id, out _);
        _youtubePending.TryRemove(id, out _);
    }

    private static Dictionary<string, string>? TakeRunnerPendingHeaders(string id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || !_runnerPendingHeaders.TryRemove(id, out Dictionary<string, string>? headers))
        {
            return null;
        }

        return headers;
    }

    private static Dictionary<string, string>? NormalizeHeaders(
        Dictionary<string, string>? headers)
    {
        if (headers is not { Count: > 0 })
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in headers)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[key] = value;
        }

        return normalized.Count == 0 ? null : normalized;
    }

    public static void RegisterYoutubePending(string id, string formatId)
        => _youtubePending[id] = new YoutubePendingMeta(formatId);

}
