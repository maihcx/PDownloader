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

namespace PDownloader.Contracts.Media;

public sealed class YtAnalyzeResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    // Duration in seconds; null for live media or unavailable metadata.
    [JsonPropertyName("duration")]
    public double? Duration { get; init; }

    [JsonPropertyName("formats")]
    public List<YtFormat>? Formats { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    public static YtAnalyzeResult Fail(string error) =>
        new() { Success = false, Error = error };
}
