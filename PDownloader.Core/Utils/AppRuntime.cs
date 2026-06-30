using PDownloader.Core.Models;

namespace PDownloader.Core.Utils
{
    public static class AppRuntime
    {
        public static ConfluxService? cfsMain   { get; set; }

        public static ConfluxService? cfsTray   { get; set; }

        public static Bootstrap? bootstrap { get; set; }

        public static Dictionary<string, ConfluxService> DownloaderCFSRest = new();

        public static ConfluxService? EnsureRunnerStarted(string token, FileTask fileTask)
        {
            DownloaderCFSRest.TryGetValue(token, out var service);
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
            svc.OnMessageReceived  += CFSCommandHandler.Handle;
            _ = svc.StartServiceAsync();

            svc.StartApp($"--token {token} --url {Helpers.Base64Encode(fileTask.url)} --save-to {Helpers.Base64Encode(fileTask.saveTo)} --filename {Helpers.Base64Encode(fileTask.fileName)} --download-runner {Helpers.Base64Encode(fileTask.downloadRunner)}");

            DownloaderCFSRest.Add(token, svc);

            return svc;
        }
    }
}
