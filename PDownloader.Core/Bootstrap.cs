using Microsoft.Extensions.Hosting;
using PDownloader.Core.Utils;

namespace PDownloader.Core
{
    public class Bootstrap
    {
        private readonly IHostApplicationLifetime lifetime;

        public Bootstrap(IHostApplicationLifetime lifetime)
        {
            this.lifetime = lifetime;
        }

        public void OnStarted()
        {
            // Wire up download manager broadcasts
            AppRuntime.InitDownloadManager();

            #region ConfluxService — PDownloader.exe (Main UI)
            ConfluxService cfsMain = new();
            cfsMain.Register(
                "PDownloader.exe",
                "PDownloader.CoreToMain",
                "PDownloader.MainToCore");
            AppRuntime.cfsMain = cfsMain;
            cfsMain.OnMessageReceiving += CFSIncomingHandler.Handle;
            cfsMain.OnMessageReceived  += CFSCommandHandler.Handle;
            _ = cfsMain.StartServiceAsync();
            #endregion

            #region ConfluxService — PDownloader Tray.exe
            ConfluxService cfsTray = new();
            cfsTray.Register(
                "PDownloader Tray.exe",
                "PDownloader.CoreToTray",
                "PDownloader.TrayToCore");
            AppRuntime.cfsTray = cfsTray;
            cfsTray.OnMessageReceiving += CFSIncomingHandler.Handle;
            cfsTray.OnMessageReceived  += CFSCommandHandler.Handle;
            cfsTray.CreateNoWindow = true;
            cfsTray.StartApp();
            _ = cfsTray.StartServiceAsync();
            #endregion

            // Runner is started on-demand when first download request arrives
        }

        public void OnStopped()
        {
            _ = AppRuntime.cfsMain?.StopServiceAsync();
            _ = AppRuntime.cfsTray?.StopServiceAsync();

            AppRuntime.cfsMain = AppRuntime.cfsTray = null;
        }

        public void Shutdown() => lifetime.StopApplication();
    }
}
