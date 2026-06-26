namespace PDownloader.Utils
{
    public static class UserDataStore
    {
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDownloader");

        private static readonly string DataFile = Path.Combine(DataDir, "userdata.json");

        private static Dictionary<string, object> _data = new();

        private static Dictionary<string, string> _passCaching = new();

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
            if (_data.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is JsonElement elem)
                        return elem.Deserialize<T>()!;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch { }
            }

            try
            {
                var defaultValue = Properties.Settings.Default[key];
                if (defaultValue is T tVal)
                    return tVal;
                return (T)Convert.ChangeType(defaultValue, typeof(T));
            }
            catch
            {
                return default!;
            }
        }

        public static string GetValuePass(string key)
        {
            try
            {
                _passCaching.TryGetValue(key, out var result);
                if (string.IsNullOrEmpty(result))
                {
                    result = GetValue<string>(key);

                    if (!string.IsNullOrEmpty(result))
                    {
                        result = PasswordEncryptor.Decrypt(result);
                        _passCaching[key] = result;
                    }
                }
                return result;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool SetValue<T>(string key, T value)
        {
            _data[key] = value!;
            SaveData();
            return true;
        }

        public static bool SetValuePass(string key, string value)
        {
            _passCaching[key] = value;
            return SetValue(key, PasswordEncryptor.Encrypt(value));
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
}
