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

namespace PDownloader.Downloads.Hls;

internal readonly record struct MediaDownloadProgress(
    long DownloadedBytes, long TotalBytes, bool IsTotalEstimated, double SpeedBps, double Percent);

internal sealed class YtDlpProgressTracker
{
    private readonly Dictionary<string, StreamState> _streams = new(StringComparer.Ordinal);
    private bool _isLive;
    private string _combinedFormatId = string.Empty;

    public void Initialize(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var previous = new Dictionary<string, StreamState>(_streams, StringComparer.Ordinal);
        _streams.Clear();
        _isLive |= MediaSizeEstimate.IsLive(metadata);
        _combinedFormatId = GetId(metadata);
        double duration = MediaSizeEstimate.PositiveNumber(metadata, "duration");
        if (metadata.TryGetProperty("requested_formats", out JsonElement formats)
            && formats.ValueKind == JsonValueKind.Array && formats.GetArrayLength() > 0)
        {
            foreach (JsonElement format in formats.EnumerateArray())
            {
                _streams[GetId(format)] = new()
                {
                    Size = MediaSizeEstimate.FromMetadata(format, duration, _isLive),
                };
            }
        }
        else
        {
            _streams[_combinedFormatId] = new()
            {
                Size = MediaSizeEstimate.FromMetadata(metadata, duration, _isLive),
            };
        }

        // stdout/stderr can arrive out of order; late metadata must not reset bytes.
        if (previous.ContainsKey(_combinedFormatId) && !_streams.ContainsKey(_combinedFormatId))
        {
            _streams.Clear();
        }

        foreach ((string? id, StreamState? state) in previous)
        {
            _streams[id] = state;
        }
    }

    public MediaDownloadProgress Update(
        string formatId, long downloadedBytes, long exactTotalBytes, long estimatedTotalBytes,
        double speedBps, bool finished, bool isLive, long fragmentIndex, long fragmentCount)
    {
        _isLive |= isLive;
        // Some downloaders merge both tracks directly and emit one combined ID.
        if (formatId == _combinedFormatId && !_streams.ContainsKey(formatId))
        {
            MediaDownloadProgress combined = Capture(0);
            _streams.Clear();
            _streams[formatId] = new() { Size = new(combined.TotalBytes, combined.IsTotalEstimated) };
        }

        if (!_streams.TryGetValue(formatId, out StreamState? stream))
        {
            _streams[formatId] = stream = new();
        }

        // Values are per selected format. Assignment also handles retries/resume;
        // completed formats stay in the dictionary instead of being reset to zero.
        stream.DownloadedBytes = downloadedBytes;
        stream.Finished = finished;
        if (finished)
        {
            stream.Size = new(downloadedBytes, false);
        }
        else if (exactTotalBytes > 0)
        {
            stream.Size = new(exactTotalBytes, false);
        }
        else if (estimatedTotalBytes > 0 && (stream.Size.Bytes <= 0 || stream.Size.IsEstimated))
        {
            stream.Size = new(Math.Max(estimatedTotalBytes, downloadedBytes), true);
        }
        else if (fragmentIndex > 0 && fragmentCount > fragmentIndex
            && (stream.Size.Bytes <= 0 || stream.Size.IsEstimated))
        {
            stream.Size = new(MediaSizeEstimate.ToBytes(
                downloadedBytes / (double)fragmentIndex * fragmentCount), true);
        }
        else if (stream.Size.Bytes > 0 && downloadedBytes > stream.Size.Bytes)
        {
            stream.Size = new(downloadedBytes, true);
        }

        stream.Fraction = finished ? 1
            : fragmentCount > 0 ? Math.Clamp(fragmentIndex / (double)fragmentCount, 0, 0.99)
            : stream.Size.Bytes > 0 ? Math.Clamp(downloadedBytes / (double)stream.Size.Bytes, 0, 0.99)
            : 0;
        return Capture(speedBps);
    }

    public MediaDownloadProgress Capture(double speedBps)
    {
        double downloaded = 0, total = 0, fractions = 0;
        bool allKnown = _streams.Count > 0, estimated = false;
        foreach (StreamState stream in _streams.Values)
        {
            downloaded += stream.DownloadedBytes;
            total += stream.Size.Bytes;
            allKnown &= stream.Size.Bytes > 0 || stream.Finished;
            estimated |= stream.Size.IsEstimated;
            fractions += stream.Fraction;
        }

        long totalBytes = !_isLive && allKnown ? MediaSizeEstimate.ToBytes(total) : 0;
        // If byte totals are missing, retain segment-based progress. Live has no endpoint.
        double percent = _isLive ? 0 : totalBytes > 0
            ? Math.Clamp(downloaded / totalBytes * 100, 0, 99)
            : _streams.Count > 0 ? Math.Min(99, fractions / _streams.Count * 100) : 0;
        return new(MediaSizeEstimate.ToBytes(downloaded), totalBytes,
            totalBytes > 0 && estimated, speedBps, percent);
    }

    private static string GetId(JsonElement metadata) =>
        metadata.ValueKind == JsonValueKind.Object
        && metadata.TryGetProperty("format_id", out JsonElement id)
        && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "default" : "default";

    private sealed class StreamState
    {
        public long DownloadedBytes;
        public MediaSizeEstimate Size;
        public double Fraction;
        public bool Finished;
    }
}
