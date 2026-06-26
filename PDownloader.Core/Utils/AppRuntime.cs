namespace PDownloader.Core.Utils
{
    public static class AppRuntime
    {
        public static ConfluxService? cfsMain   { get; set; }

        public static ConfluxService? cfsTray   { get; set; }

        public static Bootstrap? bootstrap { get; set; }

        public static Dictionary<string, ConfluxService> DownloaderCFSRest = new();

        public static ConfluxService EnsureRunnerStarted(string token)
        {
            var svc = new ConfluxService();
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
                    DownloaderCFSRest.Remove(token);
                }
            };
            svc.OnMessageReceived  += CFSCommandHandler.Handle;
            _ = svc.StartServiceAsync();

            if (!svc.IsAppStarted())
                svc.StartApp($"--token {token}");

            DownloaderCFSRest.Add(token, svc);

            return svc;
        }

        /// <summary>Hook download manager events once at startup.</summary>
        public static void InitDownloadManager()
        {
            DownloadManager.Instance.OnItemChanged += item =>
            {
                CFSIncomingHandler.BroadcastItemChanged(item);
            };
        }
    }
}
