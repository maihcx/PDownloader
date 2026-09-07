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

namespace PDownloader.TorrentShell.Utils;

/// <summary>
/// Compatibility facade matching Runner's settings access. TorrentShell reads
/// the application language from Core and never owns a separate settings file.
/// </summary>
public static class UserDataStore
{
    private static readonly ISettingsClient Client = new SettingsClient();

    public static Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Client.WaitUntilReadyAsync(cancellationToken);

    public static T GetValue<T>(string key, T defaultValue = default!) =>
        Client.GetValue(key, defaultValue);

    public static void Reload() => Client.Reload();
}
