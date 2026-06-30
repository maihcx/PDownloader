using System.IO;
using System.Text.Json;

namespace PDownloader.Runner.Utils;

public static class UserDataStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SM SOFT", "PDownloader", "runner_settings.json");

    private static Dictionary<string, string> _cache = Load();

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
                       ?? new();
        }
        catch { }
        return new();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_cache));
        }
        catch { }
    }

    public static T? GetValue<T>(string key)
    {
        if (_cache.TryGetValue(key, out var val))
        {
            try { return JsonSerializer.Deserialize<T>(val); } catch { }
        }
        return default;
    }

    public static void SetValue<T>(string key, T value)
    {
        _cache[key] = JsonSerializer.Serialize(value);
        Save();
    }
}
