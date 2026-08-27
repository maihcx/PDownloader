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

using System.IO;
using System.Text.Json;

namespace PDownloader.Installer.Utils;

public static class UserDataStore
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT", "PDownloader");

    private static readonly string DataFile = Path.Combine(DataDir, "userdata.json");

    private static Dictionary<string, object> _data = new();

    static UserDataStore()
    {
        try
        {
            if (File.Exists(DataFile))
            {
                var json = File.ReadAllText(DataFile);
                _data = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                        ?? new Dictionary<string, object>();
            }
        }
        catch
        {
            _data = new Dictionary<string, object>();
        }
    }

    private static void SaveData()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(DataFile, json);
        }
        catch { }
    }

    public static T GetValue<T>(string key)
    {
        return GetValue<T>(key, default!);
    }

    public static T GetValue<T>(string key, T defaultVal)
    {
        if (_data.TryGetValue(key, out var value))
        {
            try
            {
                if (value is JsonElement elem)
                {
                    return elem.Deserialize<T>()!;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch { }
        }

        try
        {
            var defaultValue = Properties.Settings.Default[key];
            if (defaultValue is T tVal)
            {
                return tVal;
            }

            return (T)Convert.ChangeType(defaultValue, typeof(T));
        }
        catch
        {
            if (defaultVal != null)
            {
                return defaultVal;
            }

            return default!;
        }
    }

    public static bool SetValue<T>(string key, T value)
    {
        _data[key] = value!;
        SaveData();
        return true;
    }

    public static void Reset()
    {
        _data.Clear();
        SaveData();
    }

    public static void Reload()
    {
        _data.Clear();
        if (File.Exists(DataFile))
        {
            try
            {
                var json = File.ReadAllText(DataFile);
                _data = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                        ?? new Dictionary<string, object>();
            }
            catch
            {
                _data = new Dictionary<string, object>();
            }
        }
    }
}
