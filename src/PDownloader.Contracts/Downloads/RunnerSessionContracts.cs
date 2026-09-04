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

namespace PDownloader.Contracts.Downloads;

/// <summary>
/// UI-safe Runner initialization data returned by Core after the Runner connects.
/// Sensitive download headers and format selection remain owned by Core.
/// </summary>
public sealed class RunnerSessionView
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("saveTo")]
    public string SaveTo { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("threads")]
    public int Threads { get; init; }

    [JsonPropertyName("isRunner")]
    public bool IsRunner { get; init; }

    [JsonPropertyName("categories")]
    public List<DownloadCategoryDto> Categories { get; init; } = [];

    [JsonPropertyName("selectedCategoryId")]
    public string SelectedCategoryId { get; init; } = string.Empty;
}

/// <summary>
/// User-editable values submitted by a Runner when a new download is confirmed.
/// Core derives identity, URL, headers and media format from its Runner session.
/// </summary>
public sealed class RunnerStartDownloadRequest
{
    [JsonPropertyName("saveTo")]
    public string SaveTo { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("threads")]
    public int Threads { get; init; }

    [JsonPropertyName("categoryId")]
    public string CategoryId { get; init; } = string.Empty;

    [JsonPropertyName("rememberPathForCategory")]
    public bool RememberPathForCategory { get; init; }
}
