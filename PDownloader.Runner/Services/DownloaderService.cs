namespace PDownloader.Runner.Services
{
    /// <summary>
    /// Managed host of the application.
    /// </summary>
    public class DownloaderService : IHostedService
    {
        private readonly RunnerConfig _runnerConfig;

        public ConfluxService? CfsContact;

        public DownloaderServiceStatus DownloaderStatus = new();

        public event Action<DownloadItemDto>? OnProgress;

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
            await HandleActivationAsync();
        }

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown.
        /// </summary>
        /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _ = CfsContact?.StopServiceAsync();

            if (DownloaderStatus.State == RunnerState.Form)
            {
                CfsContact?.Send("runner-cancel-exp", "0");
            }
            else
            {
                CfsContact?.Send("runner-ui-closed", "0");
            }

            await Task.CompletedTask;
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

                var payload = JsonSerializer.Serialize(new
                {
                    id = _runnerConfig.Token,
                    url = _runnerConfig.InitialUrl,
                    saveTo = _runnerConfig.SaveTo,
                    fileName = _runnerConfig.FileName,
                    threads = _runnerConfig.Threads
                });

                bool ok = await Task.Run(() => SendWithRetry(payload, retries: 3));

                DownloaderStatus.IsSending = false;

                if (ok)
                {
                    DownloaderStatus.StatusKey = "stt_download_conneting_title";
                    DownloaderStatus.State = RunnerState.Downloading;
                }
                else
                {
                    DownloaderStatus.ErrorKey = "err_download_pdcore_notvalid_title";
                    DownloaderStatus.HasError  = true;
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
            CfsContact?.Send("runner-pause", _runnerConfig.Token, TimeSpan.FromSeconds(30));
            //DownloaderStatus.IsPaused = true;
        }

        public void ResumeDownload()
        {
            if (!DownloaderStatus.IsPaused)
            {
                return;
            }
            CfsContact?.Send("runner-resume", _runnerConfig.Token, TimeSpan.FromSeconds(30));
            //DownloaderStatus.IsPaused = false;
        }

        public void CancelDownload()
        {
            CfsContact?.Send("runner-cancel", _runnerConfig.Token, TimeSpan.FromSeconds(30));
            DownloaderStatus.State = RunnerState.Form;
            //DownloaderStatus.IsPaused = false;
        }

        /// <summary>
        /// Creates main window during activation.
        /// </summary>
        private async Task HandleActivationAsync()
        {
            CfsContact = new ConfluxService();
            CfsContact.Register(
                "PDownloader Core.exe",
                $"PDownloader.RunnerToCore-{_runnerConfig.Token}",
                $"PDownloader.CoreToRunner-{_runnerConfig.Token}"
            );
            CfsContact.OnMessageReceiving += RunnerCommandHandler.Handle;

            CfsContact.OnMessageReceiving += (name, value) =>
            {
                switch (name)
                {
                    //case "cancel":
                    //    break;

                    case "download":
                        try
                        {
                            using var doc = JsonDocument.Parse(value);
                            var root = doc.RootElement;

                            string url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                            string saveTo = root.TryGetProperty("saveTo", out var s) ? s.GetString() ?? "" : "";
                            string fileName = root.TryGetProperty("fileName", out var f) ? f.GetString() ?? "" : "";

                            if (string.IsNullOrWhiteSpace(url)) return;

                            _runnerConfig.InitialUrl = url;
                            _runnerConfig.SaveTo = saveTo;
                            _runnerConfig.FileName = fileName;
                        }
                        catch { }
                        break;

                        //case "state":
                        //    if (value == "shutdown")
                        //        System.Windows.Application.Current?.Shutdown();
                        //    break;
                }
            };

            CfsContact.OnMessageReceived += (name, value) =>
            {
                switch (name)
                {
                    case "muxt-download-progress":
                        try
                        {
                            var dto = JsonSerializer.Deserialize<DownloadItemDto>(value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (dto != null)
                            {
                                Enum.TryParse(dto.Status, out DownloadStatus status);

                                DownloaderStatus.IsPaused = status == DownloadStatus.Paused;
                                DownloaderStatus.IsSending = !(status is DownloadStatus.Completed or DownloadStatus.Cancelled);
                                OnProgress?.Invoke(dto);
                            }
                        }
                        catch { }
                        break;
                }
            };

            _ = CfsContact.StartServiceAsync();

            await Task.CompletedTask;
        }

        private bool SendWithRetry(string payload, int retries)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    bool ok = CfsContact?.Send("runner-start-download", payload) ?? false;
                    if (ok) return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Runner] Send attempt {i + 1} failed: {ex.Message}");
                }

                if (i < retries - 1)
                    Thread.Sleep(500);
            }
            return false;
        }
    }
}
