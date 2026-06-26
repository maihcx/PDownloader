using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PDownloader.Core
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using IHost host = Host
                .CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<Bootstrap>();

                    services.AddHostedService<CoreBackgroundService>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}