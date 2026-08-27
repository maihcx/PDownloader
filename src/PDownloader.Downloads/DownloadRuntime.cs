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

namespace PDownloader.Downloads;

/// <summary>
/// Process-owned hooks required by the download module. Core configures these once
/// at startup so PDownloader.Downloads never needs to reference PDownloader.Core.
/// </summary>
public sealed class DownloadRuntimeOptions
{
    public Func<string?> GetDefaultDownloadFolder { get; init; } = static () => null;
    public Func<string?> GetDefaultTempFolder { get; init; } = static () => null;
    public Func<string> GetFallbackDownloadFolder { get; init; } = static () =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public Action<string, FileTask>? ShowRunner { get; init; }
}

public static class DownloadRuntime
{
    private static DownloadRuntimeOptions _options = new();

    public static void Configure(DownloadRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Volatile.Write(ref _options, options);
    }

    internal static string? DefaultDownloadFolder =>
        Volatile.Read(ref _options).GetDefaultDownloadFolder();

    internal static string? DefaultTempFolder =>
        Volatile.Read(ref _options).GetDefaultTempFolder();

    internal static string FallbackDownloadFolder =>
        Volatile.Read(ref _options).GetFallbackDownloadFolder();

    internal static void RequestRunner(string id, FileTask task) =>
        Volatile.Read(ref _options).ShowRunner?.Invoke(id, task);
}
