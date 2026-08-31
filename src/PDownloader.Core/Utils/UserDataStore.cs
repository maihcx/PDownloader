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
using PDownloader.Contracts.Settings;
using PDownloader.Shared.Persistence;

namespace PDownloader.Core.Utils;

/// <summary>
/// Settings owner for the running application. Installer is a separate direct-file
/// client; both use the same source-linked persistence and inter-process lease.
/// </summary>
public sealed class UserDataStore
{
    private readonly UserDataFile _file;

    public UserDataStore() => _file = new UserDataFile();

    public T GetValue<T>(string key) => GetValue(key, default(T)!);

    public T GetValue<T>(string key, T defaultValue)
    {
        SettingsValue result = Get(key);
        if (!result.Found || result.Value.ValueKind == JsonValueKind.Null)
            return defaultValue;
        try { return result.Value.Deserialize<T>() ?? defaultValue; }
        catch (JsonException) { return defaultValue; }
    }

    public SettingsValue Get(string key)
    {
        if (_file.TryGetValue(key, out var value)
            || UserDataDefaults.Create().TryGetValue(key, out value))
            return new SettingsValue { Found = true, Value = value };
        return new SettingsValue();
    }

    public Dictionary<string, JsonElement> GetAll()
    {
        var result = UserDataDefaults.Create();
        foreach (var entry in _file.Read())
            result[entry.Key] = entry.Value;
        return result;
    }

    public bool SetValue<T>(string key, T value) => _file.SetValue(key, value);
    public void Patch(Dictionary<string, JsonElement> values) => _file.Patch(values);
    public void Reset() => _file.Reset();
    public void Reload() => _file.Read();
}
