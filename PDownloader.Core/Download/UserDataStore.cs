using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace PDownloader.Core.Download;

internal static class UserDataStore
{
    private static readonly string _path = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "SM SOFT", "PDownloader", "core_settings.json");

    private static Dictionary<string, string> _cache = Load();

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save()
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
            try { return JsonSerializer.Deserialize<T>(val); } catch { }
        return default;
    }

    public static void SetValue<T>(string key, T value)
    {
        _cache[key] = JsonSerializer.Serialize(value);
        Save();
    }

    public static string GetDefaultDownloadFolder()
    {
        string? saved = GetValue<string>("DefaultDownloadFolder");
        if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved)) return saved;
        return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + @"\Downloads";
    }
}
