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

namespace PDownloader.Core.Utils;

/// <summary>
/// Process-scoped user-data store. The instance is owned by Core DI rather than
/// a static mutable dictionary, making persistence dependencies explicit.
/// </summary>
public sealed class UserDataStore
{
    private readonly object _sync = new();
    private readonly string _dataDir;
    private readonly string _dataFile;
    private Dictionary<string, object> _data = new();

    public UserDataStore()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SM SOFT",
            "PDownloader");
        _dataFile = Path.Combine(_dataDir, "userdata.json");
        Reload();
    }

    public T GetValue<T>(string key) => GetValue(key, default(T)!);

    public T GetValue<T>(string key, T defaultValue)
    {
        lock (_sync)
        {
            if (!_data.TryGetValue(key, out object? value))
            {
                return defaultValue;
            }

            try
            {
                if (value is JsonElement element)
                {
                    T? deserialized = element.Deserialize<T>();
                    return deserialized is null ? defaultValue : deserialized;
                }

                if (value is T typed)
                {
                    return typed;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public bool SetValue<T>(string key, T value)
    {
        lock (_sync)
        {
            _data[key] = value!;
            SaveDataLocked();
            return true;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _data.Clear();
            SaveDataLocked();
        }
    }

    public void Reload()
    {
        lock (_sync)
        {
            _data = LoadData();
        }
    }

    private Dictionary<string, object> LoadData()
    {
        if (!File.Exists(_dataFile))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            string json = File.ReadAllText(_dataFile);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    private void SaveDataLocked()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            string json = JsonSerializer.Serialize(
                _data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            File.WriteAllText(_dataFile, json);
        }
        catch
        {
            // Keep in-memory state even if persistence temporarily fails.
        }
    }
}
