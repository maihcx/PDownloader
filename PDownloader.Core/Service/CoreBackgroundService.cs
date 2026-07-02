using Microsoft.Extensions.Hosting;

namespace PDownloader.Core.Service
{
    public class CoreBackgroundService : BackgroundService
    {
        private readonly Bootstrap _bootstrap;
        private readonly HttpBridgeService _httpBridge = new();

        public CoreBackgroundService(Bootstrap bootstrap)
        {
            _bootstrap = bootstrap;
            AppRuntime.bootstrap = bootstrap;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _bootstrap.OnStarted();

            // Start HTTP bridge for browser extension (localhost:6287)
            try { _httpBridge.Start(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Core] HttpBridge failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException) { }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _httpBridge.Stop();
            _bootstrap.OnStopped();
            return base.StopAsync(cancellationToken);
        }
    }
}
