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

    public static void Handle(string name, string value)
    {
        switch (name)
        {
            case AppProtocol.MainEventMessage:
                AppRuntime.cfsTray?.Send(name, value);
                foreach ((_, ConfluxService? CFSvalue) in DownloadRunner.DownloaderCFSRest)
                {
                    CFSvalue.Send(name, value);
                }

                break;

            case AppProtocol.TrayEventMessage:
            case AppProtocol.StateMessage:
                HandleMainEvent(name, value);
                break;

            case AppProtocol.CoreServiceStateMessage:
                HandleCoreState(value);
                break;

            case AppProtocol.CoreEventMessage:
                HandleCoreEvent(value);
                break;

            case UpdateProtocol.CommandMessage:
                Program.GetRequiredService<CoreUpdateCoordinator>()
                    .HandleCommand(value);
                break;

            case DownloadProtocol.GetListCommand:
                SendListToMain();
                return;

            case DownloadProtocol.DownloadByLinkCommand:
                _ = HandleDownloadByLink(value);
                break;

            case DownloadProtocol.RunnerStartDownloadCommand:
                HandleStartDownload(value);
                return;

            case DownloadProtocol.RunnerResumeCommand:
                DownloadManager.Instance.Resume(value);
                return;

            case DownloadProtocol.RunnerRetryCommand:
                DownloadManager.Instance.Retry(value);
                return;

            case DownloadProtocol.RunnerCancelCommand:
                DownloadManager.Instance.Cancel(value);
                return;

            case DownloadProtocol.RunnerPauseCommand:
                DownloadManager.Instance.Pause(value);
                return;

            case DownloadProtocol.RunnerClearCommand:
                DownloadManager.Instance.ClearAll(value);
                return;

            case DownloadProtocol.RunnerPauseAllCommand:
                DownloadManager.Instance.PauseAll();
                return;

            case DownloadProtocol.RunnerResumeAllCommand:
                DownloadManager.Instance.ResumeAll();
                return;

            case DownloadProtocol.RunnerRetryAllCommand:
                DownloadManager.Instance.RetryAll();
                return;
        }
    }

    private static void HandleMainEvent(string name, string value)
    {
        if (!AppRuntime.cfsMain!.IsAppStarted())
        {
            mainAppAction = () =>
            {
                _ = SendPendingMainEventAsync(name, value);
            };

            AppRuntime.cfsMain.StartApp();
        }
        else
        {
            _ = AppRuntime.cfsMain.SendAsync(name, value);
        }
    }

    private static async Task SendPendingMainEventAsync(
        string name,
        string value)
    {
        ConfluxService? mainService = AppRuntime.cfsMain;
        if (mainService is not null
            && await mainService.SendAsync(name, value))
        {
            mainAppAction = null;
        }
    }

    private static void HandleCoreState(string value)
    {
        if (value == AppProtocol.State.Shutdown)
        {
            if (AppRuntime.cfsMain!.IsAppStarted())
            {
                AppRuntime.cfsMain.Send(AppProtocol.StateMessage, value);
            }

            DownloadRunner.EnsureCloseAllRunnerStarted();

            AppRuntime.bootstrap?.Shutdown();
        }
    }

    private static void HandleCoreEvent(string value)
    {
        switch (value)
        {
            case AppProtocol.CoreEvent.RefreshDownloaderConfigs:
                DownloadConfigService.Reload();
                break;

            case AppProtocol.CoreEvent.Ping:
                mainAppAction?.Invoke();
                break;
        }
    }

    private static void SendListToMain()
    {
        string json = DownloadManager.Instance.SerializeList();
        AppRuntime.cfsMain?.Send(DownloadProtocol.ListMessage, json);
    }

    private static async Task HandleDownloadByLink(string value)
    {
        StartDownloadRequest? req = JsonSerializer.Deserialize<StartDownloadRequest>(value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (req == null || string.IsNullOrWhiteSpace(req.Url))
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

    private static void HandleStartDownload(string value)
    {
        try
        {
            StartDownloadRequest? req = JsonSerializer.Deserialize<StartDownloadRequest>(value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (req == null || string.IsNullOrWhiteSpace(req.Url))
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
        catch { }
    }

    public static void BroadcastItemChanged(DownloadItem item)
    {
        object broadcastLock = _broadcastLocks.GetOrAdd(item.Id, static _ => new object());

        lock (broadcastLock)
        {
            string json = DownloadManager.SerializeItem(item);
            DownloadRunner.DownloaderCFSRest.TryGetValue(
                item.Id,
                out ConfluxService? cfsDowloaderUI);

            AppRuntime.cfsMain?.Send(DownloadProtocol.ProgressMessage, json);
            cfsDowloaderUI?.Send(DownloadProtocol.ProgressMessage, json);
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
