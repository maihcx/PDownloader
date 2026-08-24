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
    private static IHost? _host;

    static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        try
        {
            _host = Host
                .CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<Bootstrap>();
                    services.AddSingleton<DownloadConfigService>();
                    services.AddSingleton<CoreUpdateService>();
                    services.AddSingleton<CoreUpdateCoordinator>();

                    services.AddHostedService<CoreBackgroundService>();
                })
                .Build();

            await _host.RunAsync();
        }
        catch (Exception ex)
        {
            CrashHandler.Handle(ex, "Main");
        }
        finally
        {
            if (_host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                _host?.Dispose();
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

    public static T GetRequiredService<T>()
        where T : class
    {
        return _host!.Services.GetRequiredService<T>();
    }
}
