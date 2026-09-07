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

namespace PDownloader.Infrastructure.ExternalTools.YtDlp;

internal static class YtDlpJsonParser
{
    public static List<ResolvedStream> ParseResolvedStreams(string standardOutput)
    {
        string json = GetFirstJsonObject(standardOutput);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var streams = new List<ResolvedStream>();

        if (root.TryGetProperty("requested_formats", out JsonElement requestedFormats)
            && requestedFormats.ValueKind == JsonValueKind.Array
            && requestedFormats.GetArrayLength() > 0)
        {
            foreach (JsonElement element in requestedFormats.EnumerateArray())
            {
                streams.Add(ParseResolvedStream(element, root));
            }
        }
        else if (root.TryGetProperty("requested_downloads", out JsonElement requestedDownloads)
            && requestedDownloads.ValueKind == JsonValueKind.Array
            && requestedDownloads.GetArrayLength() > 0)
        {
            foreach (JsonElement element in requestedDownloads.EnumerateArray())
            {
                streams.Add(ParseResolvedStream(element, root));
            }
        }
        else
        {
            streams.Add(ParseResolvedStream(root, root));
        }

        streams.RemoveAll(stream => string.IsNullOrWhiteSpace(stream.Url));
        if (streams.Count == 0)
        {
            string preview = json.Length > 300 ? json[..300] + "..." : json;
            throw new InvalidOperationException(
                "Direct URL not found in the JSON returned by yt-dlp. " +
                "JSON (abbreviated): " + preview);
        }

        return streams;
    }

    public static YtAnalyzeResult ParseAnalysis(string standardOutput)
    {
        using JsonDocument document = JsonDocument.Parse(standardOutput);
        JsonElement root = document.RootElement;
        string title = root.GetStringOrDefault("title") ?? "video";

        if (!root.TryGetProperty("formats", out JsonElement formatArray)
            || formatArray.ValueKind != JsonValueKind.Array)
        {
            return YtAnalyzeResult.Fail("yt-dlp does not return the list of formats.");
        }

        var formats = new List<YtFormat>();
        foreach (JsonElement formatElement in formatArray.EnumerateArray())
        {
            YtFormat? format = ParseFormat(formatElement, root);
            if (format != null)
            {
                formats.Add(format);
            }
        }

        formats = formats
            // The browser consumes this order directly. Rank resolution before
            // muxed/separate video so a low-resolution muxed file cannot jump ahead
            // of a higher-resolution video that will be merged with audio.
            .OrderBy(GetFormatSortGroup)
            .ThenByDescending(format => format.Note == "Audio Only"
                ? 0 : Math.Max(0, format.Height ?? 0))
            .ThenBy(format => format.HasVideo == true && format.HasAudio == true ? 0
                : format.HasVideo == true && format.HasAudio == false ? 1 : 2)
            .ThenByDescending(format => format.Filesize)
            .ToList();

        return new YtAnalyzeResult
        {
            Success = true,
            Title = title,
            Duration = GetDuration(root),
            Formats = formats,
        };
    }

    private static int GetFormatSortGroup(YtFormat format)
    {
        if (format.HasVideo == false)
        {
            return 1;
        }

        // A known video with incomplete audio metadata still belongs with the
        // other resolutions. Entirely unknown formats follow audio-only files.
        return format.HasVideo == true || format.Height is > 0 ? 0 : 2;
    }

    public static HlsFragmentsResult? ParseHlsFragments(string standardOutput)
    {
        string json = GetFirstJsonObject(standardOutput);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement target = root;

        if (MediaSizeEstimate.IsLive(root))
        {
            return null;
        }

        if (root.TryGetProperty("requested_formats", out JsonElement requestedFormats)
            && requestedFormats.ValueKind == JsonValueKind.Array)
        {
            if (requestedFormats.GetArrayLength() > 1)
            {
                return null;
            }

            if (requestedFormats.GetArrayLength() == 1)
            {
                target = requestedFormats[0];
            }
        }

        return MediaSizeEstimate.IsLive(target) ? null : ExtractFragments(target, root);
    }

    private static ResolvedStream ParseResolvedStream(JsonElement element, JsonElement root)
    {
        string videoCodec = element.GetStringOrDefault("vcodec") ?? "none";
        string audioCodec = element.GetStringOrDefault("acodec") ?? "none";
        MediaSizeEstimate size = GetFileSize(element, root);

        return new ResolvedStream
        {
            FormatId = element.GetStringOrDefault("format_id") ?? string.Empty,
            Protocol = element.GetStringOrDefault("protocol") ?? string.Empty,
            Url = element.GetStringOrDefault("url") ?? string.Empty,
            Ext = element.GetStringOrDefault("ext") ?? "mp4",
            HasVideo = !string.Equals(videoCodec, "none", StringComparison.OrdinalIgnoreCase),
            HasAudio = !string.Equals(audioCodec, "none", StringComparison.OrdinalIgnoreCase),
            FilesizeApprox = size.Bytes,
            IsFilesizeEstimated = size.IsEstimated,
            HttpHeaders = ParseHttpHeaders(element),
        };
    }

    private static Dictionary<string, string> ParseHttpHeaders(JsonElement element)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!element.TryGetProperty("http_headers", out JsonElement headersElement)
            || headersElement.ValueKind != JsonValueKind.Object)
        {
            return headers;
        }

        foreach (JsonProperty property in headersElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(property.Name)
                && !string.IsNullOrWhiteSpace(value))
            {
                headers[property.Name] = value;
            }
        }

        return headers;
    }

    private static YtFormat? ParseFormat(JsonElement element, JsonElement root)
    {
        string extension = element.GetStringOrDefault("ext") ?? "none";
        if (extension is "mhtml" or "none")
        {
            return null;
        }

        if (!IsSupportedMediaFormat(element))
        {
            return null;
        }

        MediaSizeEstimate fileSize = GetFileSize(element, root);
        bool? hasVideo = GetCodecPresence(element, "vcodec");
        bool? hasAudio = GetCodecPresence(element, "acodec");

        // Missing/null codec metadata is unknown, not an explicit "none".
        // Track-specific measurements can establish presence, but a container
        // extension (including MP4/WebM) cannot establish which tracks exist.
        if (hasVideo is null && (HasPositiveNumber(element, "height")
            || HasPositiveNumber(element, "width")
            || HasPositiveNumber(element, "fps")
            || HasPositiveNumber(element, "vbr")))
        {
            hasVideo = true;
        }

        if (hasAudio is null && (HasPositiveNumber(element, "abr")
            || HasPositiveNumber(element, "asr")
            || HasPositiveNumber(element, "audio_channels")))
        {
            hasAudio = true;
        }

        if (hasVideo == false && hasAudio == false)
        {
            return null;
        }

        string note = hasVideo == false
            ? "Audio Only"
            : hasAudio == false || hasVideo == true && hasAudio == true
                ? string.Empty
                : "Unknown";

        int? height = element.TryGetProperty("height", out JsonElement heightElement)
            && heightElement.ValueKind == JsonValueKind.Number
                ? heightElement.GetInt32()
                : null;

        return new YtFormat
        {
            Id = element.GetStringOrDefault("format_id") ?? string.Empty,
            Ext = extension,
            Height = height,
            HasVideo = hasVideo,
            HasAudio = hasAudio,
            Note = note,
            // Older extensions forward this number as an exact size when queuing.
            // Keep estimates in the display string; the app resolves them on start.
            Filesize = fileSize.IsEstimated ? 0 : fileSize.Bytes,
            Size = (fileSize.IsEstimated ? "≈ " : string.Empty) + FormatSize(fileSize.Bytes),
        };
    }

    private static double? GetDuration(JsonElement element)
    {
        if (MediaSizeEstimate.IsLive(element))
        {
            return null;
        }

        return element.TryGetProperty("duration", out JsonElement duration)
            && duration.ValueKind == JsonValueKind.Number
            && duration.TryGetDouble(out double seconds)
            && double.IsFinite(seconds) && seconds > 0
                ? seconds
                : null;
    }

    private static bool? GetCodecPresence(JsonElement element, string propertyName)
    {
        string? codec = element.GetStringOrDefault(propertyName)?.Trim();
        if (string.IsNullOrEmpty(codec)
            || codec.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return !codec.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPositiveNumber(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
        && number > 0;

    private static bool IsSupportedMediaFormat(JsonElement element)
    {
        string protocol = element.GetStringOrDefault("protocol") ?? string.Empty;
        string? url = element.GetStringOrDefault("url");

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(protocol)
            || protocol.Equals("http", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("https", StringComparison.OrdinalIgnoreCase)
            || protocol.StartsWith("m3u8", StringComparison.OrdinalIgnoreCase)
            || protocol.Contains("dash", StringComparison.OrdinalIgnoreCase);
    }

    private static HlsFragmentsResult? ExtractFragments(JsonElement element, JsonElement root)
    {
        if (!element.TryGetProperty("fragments", out JsonElement fragmentArray)
            || fragmentArray.ValueKind != JsonValueKind.Array
            || fragmentArray.GetArrayLength() == 0)
        {
            return null;
        }

        string? baseUrl = element.GetStringOrDefault("fragment_base_url");
        string extension = element.GetStringOrDefault("ext") ?? "ts";
        var urls = new List<string>();
        var durations = new List<double>();

        foreach (JsonElement fragment in fragmentArray.EnumerateArray())
        {
            // The simple concatenating downloader cannot safely handle ranges
            // or encryption. Let yt-dlp handle these rather than fetch whole files.
            if (fragment.TryGetProperty("byte_range", out _)
                || fragment.TryGetProperty("decrypt_info", out _))
            {
                return null;
            }

            string? resolvedUrl = fragment.GetStringOrDefault("url");
            string? fragmentPath = fragment.GetStringOrDefault("path");

            if (string.IsNullOrEmpty(resolvedUrl)
                && !string.IsNullOrEmpty(fragmentPath)
                && Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri)
                && Uri.TryCreate(baseUri, fragmentPath, out Uri? absoluteUri))
            {
                resolvedUrl = absoluteUri.AbsoluteUri;
            }

            if (!string.IsNullOrEmpty(resolvedUrl))
            {
                urls.Add(resolvedUrl);
                durations.Add(MediaSizeEstimate.PositiveNumber(fragment, "duration"));
            }
            else
            {
                return null; // Never silently omit a segment from a finite playlist.
            }
        }

        MediaSizeEstimate size = GetFileSize(element, root);
        return urls.Count == 0
            ? null
            : new HlsFragmentsResult
            {
                FragmentUrls = urls,
                FragmentDurations = durations,
                Size = size,
                Ext = extension,
            };
    }

    private static string GetFirstJsonObject(string standardOutput)
    {
        string? json = standardOutput
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith('{'));

        return json ?? throw new InvalidOperationException(
            "yt-dlp does not return valid JSON.");
    }

    private static MediaSizeEstimate GetFileSize(JsonElement element, JsonElement root) =>
        MediaSizeEstimate.FromMetadata(element,
            MediaSizeEstimate.PositiveNumber(root, "duration"), MediaSizeEstimate.IsLive(root));

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }

        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
