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

using PDownloader.Contracts.Ipc;

namespace PDownloader.Contracts.Settings;

/// <summary>Settings are owned by Core. Clients send key patches, never a replacement file.</summary>
public static class SettingsProtocol
{
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    public static readonly IpcRequestDefinition<IpcNoPayload, bool> Ping = new("settings.ping");
    public static readonly IpcRequestDefinition<string, SettingsValue> Get = new("settings.get");
    public static readonly IpcRequestDefinition<IpcNoPayload, Dictionary<string, JsonElement>> GetAll = new("settings.get-all");
    public static readonly IpcRequestDefinition<Dictionary<string, JsonElement>, IpcNoPayload> Patch = new("settings.patch");
    public static readonly IpcRequestDefinition<IpcNoPayload, IpcNoPayload> Reset = new("settings.reset");
}

public sealed record SettingsValue
{
    public bool Found { get; init; }
    public JsonElement Value { get; init; } = JsonSerializer.SerializeToElement<object?>(null);
}
