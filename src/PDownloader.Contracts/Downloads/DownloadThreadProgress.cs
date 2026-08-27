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

using System.Text.Json.Serialization;

namespace PDownloader.Contracts.Downloads;

public sealed record DownloadThreadProgress(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("downloadedBytes")] long DownloadedBytes,
    [property: JsonPropertyName("totalBytes")] long TotalBytes,
    [property: JsonPropertyName("speedBps")] double SpeedBps,
    [property: JsonPropertyName("state")]
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DownloadThreadState State,
    [property: JsonPropertyName("currentUnit")] int CurrentUnit = 0,
    [property: JsonPropertyName("totalUnits")] int TotalUnits = 0)
{
    [JsonPropertyName("progress")]
    public double Progress => TotalBytes > 0
        ? Math.Clamp(DownloadedBytes / (double)TotalBytes * 100.0, 0, 100)
        : 0;
}
