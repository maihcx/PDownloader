namespace PDownloader.Core.Runtime
{
    public static class CFSCommandHandler
    {
        public static void Handle(string name, string value)
        {
            switch (name)
            {
                case "main-event":
                    AppRuntime.cfsTray?.Send(name, value);
                    foreach (var (CFSkey, CFSvalue) in AppRuntime.DownloaderCFSRest)
                    {
                        CFSvalue.Send(name, value);
                    }
                    break;

                case "tray-event":
                case "state":
                    HandleMainEvent(name, value);
                    break;

                case "core-svc-state":
                    HandleCoreState(value);
                    break;

                case "runner-resume":
                    HandleShowRunnerForDownload(value);
                    DownloadManager.Instance.Resume(value);
                    return;

                case "runner-retry":
                    DownloadManager.Instance.Retry(value);
                    return;

                case "runner-cancel":
                    DownloadManager.Instance.Cancel(value);
                    return;

                case "runner-pause":
                    DownloadManager.Instance.Pause(value);
                    return;
            }
        }

        private static void HandleMainEvent(string name, string value)
        {
            if (!AppRuntime.cfsMain!.IsAppStarted())
                AppRuntime.cfsMain.StartApp();

            AppRuntime.cfsMain.Send(name, value);
        }

        private static void HandleCoreState(string value)
        {
            if (value == "shutdown")
            {
                if (AppRuntime.cfsMain!.IsAppStarted())
                {
                    AppRuntime.cfsMain.Send("state", value);
                }

                AppRuntime.bootstrap?.Shutdown();
            }
        }

        private static void HandleShowRunnerForDownload(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            DownloadItem? downloadItem = DownloadManager.Instance.Find(value);
            if (downloadItem != null)
            {
                AppRuntime.EnsureRunnerStarted(downloadItem.Id, new()
                {
                    id = downloadItem.Id,
                    fileName = downloadItem.FileName,
                    formatId = downloadItem.FormatId ?? string.Empty,
                    filesize = downloadItem.TotalBytes,
                    saveTo = downloadItem.SavePath,
                    url = downloadItem.Url,
                    downloadRunner = "runner",
                });
            }
        }
    }
}
