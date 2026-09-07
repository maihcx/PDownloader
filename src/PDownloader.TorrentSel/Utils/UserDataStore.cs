// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.TorrentSel.Utils;

/// <summary>
/// Compatibility facade matching Runner's settings access. TorrentSel reads
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
