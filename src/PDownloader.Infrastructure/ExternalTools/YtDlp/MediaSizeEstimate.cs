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

using System.Text.Json;

namespace PDownloader.Infrastructure.ExternalTools.YtDlp;

// Bytes describe the selected stream, not the eventual muxed output file.
public readonly record struct MediaSizeEstimate(long Bytes, bool IsEstimated)
{
    public static bool IsLive(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && ((element.TryGetProperty("is_live", out JsonElement live)
                && live.ValueKind == JsonValueKind.True)
            || (element.TryGetProperty("live_status", out JsonElement status)
                && status.ValueKind == JsonValueKind.String
                && status.GetString() == "is_live"));

    public static double PositiveNumber(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
        && double.IsFinite(number) && number > 0 ? number : 0;

    public static MediaSizeEstimate FromMetadata(
        JsonElement element, double duration = 0, bool isLive = false)
    {
        // A live playlist is open-ended, even if an extractor returns a window size.
        if (isLive || IsLive(element))
        {
            return default;
        }

        long exact = ReadBytes(element, "filesize");
        if (exact > 0)
        {
            return new(exact, false);
        }

        long approximate = ReadBytes(element, "filesize_approx");
        if (approximate > 0)
        {
            return new(approximate, true);
        }

        double ownDuration = PositiveNumber(element, "duration");
        if (ownDuration > 0)
        {
            duration = ownDuration;
        }

        double bitrate = PositiveNumber(element, "tbr");
        // Only use track-specific rates when all present tracks are accounted for.
        if (bitrate <= 0)
        {
            double video = PositiveNumber(element, "vbr");
            double audio = PositiveNumber(element, "abr");
            bool noVideo = IsNone(element, "vcodec");
            bool noAudio = IsNone(element, "acodec");
            if ((video > 0 || noVideo) && (audio > 0 || noAudio))
            {
                bitrate = (noVideo ? 0 : video) + (noAudio ? 0 : audio);
            }
        }

        // yt-dlp rates are kilobits/second. This is deliberately labelled approximate.
        long bytes = ToBytes(duration * bitrate * 1000 / 8);
        return bytes > 0 ? new(bytes, true) : default;
    }

    public static long ToBytes(double value) =>
        double.IsFinite(value) && value > 0 && value < long.MaxValue
            ? (long)value : 0;

    private static long ReadBytes(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long bytes))
        {
            return Math.Max(0, bytes);
        }

        return ToBytes(PositiveNumber(element, name));
    }

    private static bool IsNone(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && string.Equals(value.GetString(), "none", StringComparison.OrdinalIgnoreCase);
}
