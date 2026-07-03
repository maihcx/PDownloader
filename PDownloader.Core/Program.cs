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
using PDownloader.Core.Services.DownloadServices;

namespace PDownloader.Core;

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