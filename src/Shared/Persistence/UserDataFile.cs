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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace PDownloader.Shared.Persistence;

/// <summary>
/// Source-linked only into Core and Installer. No IPC or application dependencies.
/// Each read/patch/reset/delete uses the same inter-process file lease. Writes reload
/// inside that lease, flush an adjacent temporary file, then replace atomically.
/// </summary>
internal sealed class UserDataFile
{
    private const int MaxFileBytes = 512 * 1024;
    private readonly object _sync = new();
    private readonly string _dataDir;
    private readonly string _dataFile;
    private readonly string _lockFile;

    public UserDataFile() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT", "PDownloader")) { }

    private UserDataFile(string dataDirectory)
    {
        _dataDir = Path.GetFullPath(dataDirectory);
        _dataFile = Path.Combine(_dataDir, "userdata.json");
        // Keep this stable and outside the data directory, including during uninstall.
        _lockFile = _dataDir + ".settings-lock";
    }

    public Dictionary<string, JsonElement> Read() => WithLock(LoadData);

    public bool TryGetValue(string key, out JsonElement value)
    {
        ValidateKey(key);
        return Read().TryGetValue(key, out value);
    }

    public T GetValue<T>(string key, T defaultValue = default!)
    {
        if (!TryGetValue(key, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        try { return value.Deserialize<T>() ?? defaultValue; }
        catch (JsonException) { return defaultValue; }
    }

    public bool SetValue<T>(string key, T value)
    {
        Patch(new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value) });
        return true;
    }

    public void Patch(Dictionary<string, JsonElement> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (KeyValuePair<string, JsonElement> entry in values)
        {
            ValidateKey(entry.Key);
            if (entry.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new ArgumentException("A setting must contain a JSON value.");
            }
        }

        WithLock(() =>
        {
            Dictionary<string, JsonElement> data = LoadData();
            foreach (KeyValuePair<string, JsonElement> entry in values)
            {
                data[entry.Key] = entry.Value.Clone();
            }

            SaveData(data);
            return true;
        });
    }

    public void Reset() => WithLock(() =>
    {
        SaveData(new Dictionary<string, JsonElement>());
        return true;
    });

    // Called directly by Installer, after stopping the installed application.
    public void DeleteUserData() => WithLock(() =>
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }

        return true;
    });

    private T WithLock<T>(Func<T> action)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_lockFile)!);
            var timer = Stopwatch.StartNew();
            FileStream lease;
            while (true)
            {
                try
                {
                    lease = new FileStream(_lockFile, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                    break;
                }
                catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Thread.Sleep(25);
                }
            }

            using (lease)
            {
                return action();
            }
        }
    }

    private Dictionary<string, JsonElement> LoadData()
    {
        if (!File.Exists(_dataFile))
        {
            return new Dictionary<string, JsonElement>();
        }

        if (new FileInfo(_dataFile).Length > MaxFileBytes)
        {
            throw new InvalidDataException("The settings file exceeds the supported size.");
        }
        // Never silently replace a corrupt/unreadable file with an empty dictionary.
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllBytes(_dataFile))
            ?? throw new InvalidDataException("The settings file must contain a JSON object.");
    }

    private void SaveData(Dictionary<string, JsonElement> data)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(data,
            new JsonSerializerOptions { WriteIndented = true });
        if (json.Length > MaxFileBytes)
        {
            throw new InvalidDataException("Settings exceed the supported size.");
        }

        Directory.CreateDirectory(_dataDir);
        string temporary = _dataFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_dataFile))
            {
                File.Replace(temporary, _dataFile, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, _dataFile);
            }
        }
        finally
        {
            try { if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException) { /* An orphan temp file does not invalidate a committed write. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 256)
        {
            throw new ArgumentException("Setting keys cannot exceed 256 characters.", nameof(key));
        }
    }
}
