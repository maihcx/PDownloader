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

namespace PDownloader.CoreClient.Settings;

/// <summary>
/// Application-facing settings API. Implementations communicate with Core;
/// consumers do not own persistence or depend on the IPC transport implementation.
/// </summary>
public interface ISettingsClient : IDisposable
{
    Task WaitUntilReadyAsync(CancellationToken cancellationToken = default);
    Task<T> GetValueAsync<T>(string key, T defaultValue = default!,
        CancellationToken cancellationToken = default);
    T GetValue<T>(string key, T defaultValue = default!);
    Task SetValueAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    bool SetValue<T>(string key, T value);
    void SetValues(IReadOnlyDictionary<string, object?> values);
    Task PatchAsync(Dictionary<string, JsonElement> values, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
    void Reset();
    void Reload();
}
