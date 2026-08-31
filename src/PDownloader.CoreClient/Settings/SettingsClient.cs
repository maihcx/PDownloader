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

using System.Text.Json;
using PDownloader.CFS;
using PDownloader.Contracts.Ipc;
using PDownloader.Contracts.Settings;

namespace PDownloader.CoreClient.Settings;

/// <summary>
/// IPC-only settings access. No disk fallback, local settings cache or retry of writes:
/// a timed-out write may already have committed and must not overwrite a newer write.
/// </summary>
public sealed class SettingsClient : ISettingsClient
{
    private readonly ConfluxService _connection = new()
    {
        MaxMessageBytes = SettingsProtocol.MaxMessageBytes,
    };

    public SettingsClient()
    {
        _connection.Register(IpcTopology.CoreProcessName,
            IpcTopology.SettingsPipeName(IpcUserScope.CurrentUserId),
            "PDownloader.SettingsClient-" + Guid.NewGuid().ToString("N"));
        // Request replies use the same connection; clients do not open a server.
    }

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        await _connection.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<T> GetValueAsync<T>(string key, T defaultValue = default!,
        CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(SettingsProtocol.Get, key, cancellationToken).ConfigureAwait(false);
        if (!result.Found || result.Value.ValueKind == JsonValueKind.Null)
            return defaultValue;
        try
        {
            return result.Value.Deserialize<T>() ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public T GetValue<T>(string key, T defaultValue = default!) =>
        GetValueAsync(key, defaultValue).GetAwaiter().GetResult();

    public Task SetValueAsync<T>(string key, T value, CancellationToken cancellationToken = default) =>
        PatchAsync(new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value) },
            cancellationToken);

    public bool SetValue<T>(string key, T value)
    {
        SetValueAsync(key, value).GetAwaiter().GetResult();
        return true; // Only after Core has persisted the change and replied.
    }

    public void SetValues(IReadOnlyDictionary<string, object?> values) =>
        PatchAsync(values.ToDictionary(pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value))).GetAwaiter().GetResult();

    public async Task PatchAsync(Dictionary<string, JsonElement> values,
        CancellationToken cancellationToken = default) =>
        _ = await CallAsync(SettingsProtocol.Patch, values, cancellationToken).ConfigureAwait(false);

    public async Task ResetAsync(CancellationToken cancellationToken = default) =>
        _ = await CallAsync(SettingsProtocol.Reset, new IpcNoPayload(), cancellationToken).ConfigureAwait(false);

    public void Reset() => ResetAsync().GetAwaiter().GetResult();

    // Kept for existing UI change notifications. Reads always query Core, so no stale cache exists.
    public void Reload() => CallAsync(SettingsProtocol.GetAll, new IpcNoPayload()).GetAwaiter().GetResult();

    private async Task<TResponse> CallAsync<TRequest, TResponse>(
        IpcRequestDefinition<TRequest, TResponse> definition, TRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _connection.RequestAsync(definition, request,
            TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Value is null)
            throw new IOException($"Core settings request '{definition.Name}' failed: {result.Error ?? "empty reply"}");
        return result.Value;
    }

    public void Dispose() => _connection.Dispose();
}
