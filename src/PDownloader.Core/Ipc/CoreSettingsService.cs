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

using Microsoft.Extensions.Hosting;
using PDownloader.Contracts.Settings;

namespace PDownloader.Core.Ipc;

public sealed class CoreSettingsService : IHostedService, IDisposable
{
    private readonly ConfluxService _server = new() { MaxMessageBytes = SettingsProtocol.MaxMessageBytes };

    public CoreSettingsService(UserDataStore store)
    {
        string pipeName = IpcTopology.SettingsPipeName(IpcUserScope.CurrentUserId);
        _server.Register(IpcTopology.CoreProcessName, pipeName + "-unused", pipeName);
        _server.RegisterRequestHandler(SettingsProtocol.Ping, () => true);
        _server.RegisterRequestHandler(SettingsProtocol.Get, store.Get);
        _server.RegisterRequestHandler(SettingsProtocol.GetAll, store.GetAll);
        _server.RegisterRequestHandler(SettingsProtocol.Patch, values =>
        {
            store.Patch(values);
            return new IpcNoPayload();
        });
        _server.RegisterRequestHandler(SettingsProtocol.Reset, () =>
        {
            store.Reset();
            return new IpcNoPayload();
        });
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartServiceAsync();
    public Task StopAsync(CancellationToken cancellationToken) => _server.StopServiceAsync();
    public void Dispose() => _server.Dispose();
}
