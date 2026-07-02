using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PDownloader.Core.Services.DownloadServices;

namespace PDownloader.Core
{
    internal class Program
    {
        private static IHost? _host;

        static async Task Main(string[] args)
        {
            _host = Host
                .CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<Bootstrap>();
                    services.AddSingleton<DownloadConfigService>();

                    services.AddHostedService<CoreBackgroundService>();
                })
                .Build();

            await _host.RunAsync();
        }

        public static T GetRequiredService<T>()
            where T : class
        {
            return _host!.Services.GetRequiredService<T>();
        }
    }
}