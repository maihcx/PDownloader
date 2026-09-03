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

using PDownloader.Shared.Persistence;
using System.Text.Json;

namespace PDownloader.Installer.Utils;

/// <summary>Standalone installer settings access; no Core process, IPC or settings payload.</summary>
public static class UserDataStore
{
    private static readonly UserDataFile FileStore = new();

    public static IReadOnlyDictionary<string, JsonElement> ReadSnapshot() => FileStore.Read();

    public static T GetValue<T>(string key) => FileStore.GetValue<T>(key);
    public static T GetValue<T>(string key, T defaultValue) => FileStore.GetValue(key, defaultValue);
    public static bool SetValue<T>(string key, T value) => FileStore.SetValue(key, value);
    public static void SetValues(IReadOnlyDictionary<string, object?> values) =>
        FileStore.Patch(values.ToDictionary(pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value)));
    public static void Reset() => FileStore.Reset();
    public static void Reload() => FileStore.Read();
    public static void DeleteUserData() => FileStore.DeleteUserData();

}
