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

namespace PDownloader.Core.Runtime;

public class DownloadRunner
{
    public static Dictionary<string, ConfluxService> DownloaderCFSRest = new();

    public static ConfluxService? EnsureRunnerStarted(string token, RunnerDownloadTask task)
    {
        DownloaderCFSRest.TryGetValue(token, out ConfluxService? service);
        if (service != null)
        {
            return null;
        }

        var svc = new ConfluxService();
        svc.CanMultiple = true;
        svc.Register(
            IpcTopology.RunnerProcessName,
            IpcTopology.CoreToRunnerPipeName(token),
            IpcTopology.RunnerToCorePipeName(token));
        svc.OnMessageReceived += message =>
        {
            if (message.Is(DownloadProtocol.RunnerCancelExperience))
            {
                _ = svc.StopServiceAsync();
                CFSCommandHandler.ClearRunnerPendingContext(token);
                DownloaderCFSRest.Remove(token);
                svc.GetProcess().Kill();
            }
            else if (message.Is(DownloadProtocol.RunnerUiClosed))
            {
                _ = svc.StopServiceAsync();
                CFSCommandHandler.ClearRunnerPendingContext(token);
                DownloaderCFSRest.Remove(token);
                svc.GetProcess().Kill();
            }
        };
        svc.OnMessageReceived += CFSCommandHandler.Handle;
        _ = svc.StartServiceAsync();

        CFSCommandHandler.RegisterRunnerPendingHeaders(token, task.Headers);

        if (task.Threads == 0)
        {
            task.Threads = CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultThreadCount ?? 8;
        }

        if (task.Threads == 0)
        {
            task.Threads = 8;
        }

        svc.StartApp(
            $"{RunnerLaunchProtocol.TokenArgument} {token} " +
            $"{RunnerLaunchProtocol.UrlArgument} {Helpers.Base64Encode(task.Url)} " +
            $"{RunnerLaunchProtocol.ThreadsArgument} {Helpers.Base64Encode(task.Threads.ToString())} " +
            $"{RunnerLaunchProtocol.SaveToArgument} {Helpers.Base64Encode(task.SaveTo)} " +
            $"{RunnerLaunchProtocol.FileNameArgument} {Helpers.Base64Encode(task.FileName)} " +
            $"{RunnerLaunchProtocol.DownloadRunnerArgument} {Helpers.Base64Encode(task.RunnerMode)}");

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
                service.Send(
                    AppProtocol.State,
                    AppState.Shutdown);
            }
        }
    }
}
