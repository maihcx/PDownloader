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

public sealed class YtFormat
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("ext")]
    public string Ext { get; init; } = "";

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    // null means the extractor did not determine whether this stream exists.
    [JsonPropertyName("hasVideo")]
    public bool? HasVideo { get; init; }

    [JsonPropertyName("hasAudio")]
    public bool? HasAudio { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } = "";

    [JsonPropertyName("size")]
    public string Size { get; init; } = "";

    [JsonPropertyName("filesize")]
    public long Filesize { get; init; }
}
