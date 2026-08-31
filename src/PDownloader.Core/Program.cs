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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PDownloader.Core.Services;
using PDownloader.Core.Services.DownloadServices;

namespace PDownloader.Core;

internal class Program
{
    static void Main(string[] args)
    {
        // Mutex ownership is thread-affine. Keep acquisition and release on this
        // entry thread while the async host runs on its normal continuations.
        using var instance = new Mutex(false,
            @"Global\PDownloader.Core-" + IpcUserScope.CurrentUserId);
        bool ownsInstance;
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance) return;
        try { RunAsync(args).GetAwaiter().GetResult(); }
        finally { instance.ReleaseMutex(); }
    }

    private static async Task RunAsync(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        IHost? host = null;

        try
        {
            host = Host
                .CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<UserDataStore>();
                    services.AddSingleton<DownloadConfigService>();
                    services.AddSingleton<YtDlpService>();
                    services.AddSingleton<IDownloadRuntime, CoreDownloadRuntime>();
                    services.AddSingleton<DownloadManager>();

                    services.AddSingleton<CoreIpcHost>();
                    services.AddSingleton<MainAppGateway>();
                    services.AddSingleton<RunnerSessionManager>();
                    services.AddSingleton<AppEventRelay>();
                    services.AddSingleton<CoreLifecycleService>();

                    services.AddSingleton<DownloadCommandService>();
                    services.AddSingleton<DownloadLaunchService>();
                    services.AddSingleton<DownloadProgressPublisher>();
                    services.AddSingleton<DownloadManagerBootstrap>();

                    services.AddSingleton<CoreUpdateService>();
                    services.AddSingleton<CoreUpdateCoordinator>();
                    services.AddSingleton<CoreIpcBindings>();
                    services.AddSingleton<Bootstrap>();
                    services.AddSingleton<HttpBridgeService>();

                    services.AddHostedService<CoreSettingsService>();
                    services.AddHostedService<CoreBackgroundService>();
                })
                .Build();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            CrashHandler.Handle(ex, "Main");
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host?.Dispose();
            }
        }
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            CrashHandler.Handle(ex, "AppDomain");
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashHandler.WriteOnly(e.Exception, "TaskScheduler");
        e.SetObserved();
    }

}
