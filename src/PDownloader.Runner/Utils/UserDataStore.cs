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

using PDownloader.CoreClient.Settings;

namespace PDownloader.Runner.Utils;

/// <summary>Compatibility facade for existing callers; all storage operations go to Core via IPC.</summary>
public static class UserDataStore
{
    private static readonly ISettingsClient Client = new SettingsClient();

    public static Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Client.WaitUntilReadyAsync(cancellationToken);

    public static T GetValue<T>(string key) => Client.GetValue<T>(key);
    public static T GetValue<T>(string key, T defaultValue) => Client.GetValue(key, defaultValue);
    public static bool SetValue<T>(string key, T value) => Client.SetValue(key, value);
    public static void SetValues(IReadOnlyDictionary<string, object?> values) => Client.SetValues(values);
    public static void Reset() => Client.Reset();
    public static void Reload() => Client.Reload();

}
