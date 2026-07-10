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

namespace PDownloader.Core.Utils;

public static class AppRuntime
{
    public static ConfluxService? cfsMain { get; set; }

    public static ConfluxService? cfsTray { get; set; }

    public static Bootstrap? bootstrap { get; set; }

    public static Dictionary<string, ConfluxService> DownloaderCFSRest = new();

    public static ConfluxService? EnsureRunnerStarted(string token, FileTask fileTask)
    {
        DownloaderCFSRest.TryGetValue(token, out ConfluxService? service);
        if (service != null)
        {
            return null;
        }

        var svc = new ConfluxService();
        svc.CanMultiple = true;
        svc.Register(
            "PDownloader Runner.exe",
            $"PDownloader.CoreToRunner-{token}",
            $"PDownloader.RunnerToCore-{token}");
        svc.OnMessageReceiving += CFSIncomingHandler.Handle;
        svc.OnMessageReceiving += (name, value) =>
        {
            if (name == "runner-cancel-exp")
            {
                _ = svc.StopServiceAsync();
                svc.GetProcess().Kill();
                DownloaderCFSRest.Remove(token);
            }
            else if (name == "runner-ui-closed")
            {
                _ = svc.StopServiceAsync();
                svc.GetProcess().Kill();
                DownloaderCFSRest.Remove(token);
            }
        };
        svc.OnMessageReceived += CFSCommandHandler.Handle;
        _ = svc.StartServiceAsync();

        string headersArg = string.Empty;
        if (fileTask.headers is { Count: > 0 })
        {
            string headersJson = System.Text.Json.JsonSerializer.Serialize(fileTask.headers);
            headersArg = $" --headers {Helpers.Base64Encode(headersJson)}";
        }

        if (fileTask.threads == 0)
        {
            fileTask.threads = CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultThreadCount ?? 8;
        }

        if (fileTask.threads == 0)
        {
            fileTask.threads = 8;
        }

        svc.StartApp($"--token {token} --url {Helpers.Base64Encode(fileTask.url)} --threads {Helpers.Base64Encode(fileTask.threads.ToString())} --save-to {Helpers.Base64Encode(fileTask.saveTo)} --filename {Helpers.Base64Encode(fileTask.fileName)} --download-runner {Helpers.Base64Encode(fileTask.downloadRunner)}{headersArg}");

        DownloaderCFSRest.Add(token, svc);

        return svc;
    }

    public static void EnsureCloseAllRunnerStarted()
    {
        foreach (KeyValuePair<string, ConfluxService> item in DownloaderCFSRest)
        {
            string key = item.Key;
            using (ConfluxService service = item.Value)
            {
                service.Send("state", "shutdown");
            }
        }
    }
}
