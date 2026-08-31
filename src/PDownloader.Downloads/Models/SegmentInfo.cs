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

namespace PDownloader.Downloads.Models;

public class SegmentInfo
{
    public int Index { get; init; }

    public long RangeStart { get; init; }

    public long RangeEnd { get; init; }

    public long BytesWritten { get; set; }

    public string TempFilePath { get; init; } = string.Empty;

    public bool IsCompleted { get; set; }

    [JsonIgnore]
    public DownloadThreadState TransferState { get; set; } = DownloadThreadState.Waiting;

    [JsonIgnore]
    public int RetryAttempt { get; set; }

    public long Length => RangeEnd - RangeStart + 1;

    // Only this committed pair is persisted; live progress may be ahead of it.
    private readonly object _checkpointLock = new();
    private long _committedBytes;
    private bool _committedCompletion;

    internal void CommitCheckpoint(long bytesWritten, bool isCompleted)
    {
        lock (_checkpointLock)
        {
            _committedBytes = bytesWritten;
            _committedCompletion = isCompleted;
        }
    }

    internal SegmentInfo CaptureCheckpoint()
    {
        lock (_checkpointLock)
        {
            return new SegmentInfo
            {
                Index = Index,
                RangeStart = RangeStart,
                RangeEnd = RangeEnd,
                TempFilePath = TempFilePath,
                BytesWritten = _committedBytes,
                IsCompleted = _committedCompletion
            };
        }
    }
}
