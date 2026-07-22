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

namespace PDownloader.Core.Download.Segments;

internal sealed class SegmentStateStore
{
    private const string StateFileName = "segments.pdstate";

    public List<SegmentInfo> BuildOrRestore(
        string tempDirectory,
        long totalBytes,
        int threadCount)
    {
        Directory.CreateDirectory(tempDirectory);

        List<SegmentInfo>? restored = TryRestore(tempDirectory, totalBytes, threadCount);
        if (restored != null)
        {
            return restored;
        }

        Reset(tempDirectory);
        return BuildNew(tempDirectory, totalBytes, threadCount);
    }

    public void Persist(string tempDirectory, IReadOnlyCollection<SegmentInfo> segments)
    {
        try
        {
            string stateFile = GetStateFilePath(tempDirectory);
            File.WriteAllText(stateFile, JsonSerializer.Serialize(segments));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Segments] Unable to save state: {ex.Message}");
        }
    }

    public void Reset(string tempDirectory)
    {
        try
        {
            if (!Directory.Exists(tempDirectory))
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(tempDirectory, "seg_*.part"))
            {
                try { File.Delete(path); } catch { }
            }

            string stateFile = GetStateFilePath(tempDirectory);
            if (File.Exists(stateFile))
            {
                File.Delete(stateFile);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Segments] Unable to reset status: {ex.Message}");
        }
    }

    private List<SegmentInfo>? TryRestore(
        string tempDirectory,
        long totalBytes,
        int threadCount)
    {
        string stateFile = GetStateFilePath(tempDirectory);
        if (!File.Exists(stateFile))
        {
            return null;
        }

        try
        {
            List<SegmentInfo>? saved = JsonSerializer.Deserialize<List<SegmentInfo>>(
                File.ReadAllText(stateFile));

            if (saved == null || saved.Count != threadCount)
            {
                return null;
            }

            if (!RangesMatch(saved, totalBytes, threadCount))
            {
                return null;
            }

            foreach (SegmentInfo segment in saved)
            {
                long actualLength = File.Exists(segment.TempFilePath)
                    ? new FileInfo(segment.TempFilePath).Length
                    : 0;

                if (actualLength != segment.BytesWritten)
                {
                    Debug.WriteLine(
                        $"[Segments] Segment {segment.Index}: state={segment.BytesWritten}B, " +
                        $"actual={actualLength}B. Synchronize by file.");
                    segment.BytesWritten = actualLength;
                }

                long expectedLength = segment.RangeEnd >= 0
                    ? segment.RangeEnd - segment.RangeStart + 1
                    : -1;

                if (expectedLength > 0)
                {
                    if (actualLength > expectedLength)
                    {
                        return null;
                    }

                    segment.IsCompleted = actualLength == expectedLength;
                }
                else if (actualLength == 0)
                {
                    segment.IsCompleted = false;
                }

                segment.TransferState = segment.IsCompleted
                    ? DownloadThreadState.Completed
                    : DownloadThreadState.Waiting;
                segment.RetryAttempt = 0;
            }

            return saved;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Segments] Unable to restore state: {ex.Message}");
            return null;
        }
    }

    private static bool RangesMatch(
        IReadOnlyList<SegmentInfo> segments,
        long totalBytes,
        int threadCount)
    {
        List<SegmentInfo> expected = BuildNew(
            Path.GetDirectoryName(segments[0].TempFilePath) ?? string.Empty,
            totalBytes,
            threadCount);

        return segments.Count == expected.Count
            && segments.Zip(expected).All(pair =>
                pair.First.Index == pair.Second.Index
                && pair.First.RangeStart == pair.Second.RangeStart
                && pair.First.RangeEnd == pair.Second.RangeEnd
                && Path.GetFullPath(pair.First.TempFilePath).Equals(
                    Path.GetFullPath(pair.Second.TempFilePath),
                    StringComparison.OrdinalIgnoreCase));
    }

    private static List<SegmentInfo> BuildNew(
        string tempDirectory,
        long totalBytes,
        int threadCount)
    {
        threadCount = Math.Max(1, threadCount);
        var segments = new List<SegmentInfo>(threadCount);

        if (threadCount == 1 || totalBytes <= 0)
        {
            segments.Add(new SegmentInfo
            {
                Index = 0,
                RangeStart = 0,
                RangeEnd = totalBytes > 0 ? totalBytes - 1 : -1,
                TempFilePath = Path.Combine(tempDirectory, "seg_0.part"),
            });

            return segments;
        }

        long chunkSize = totalBytes / threadCount;
        for (int index = 0; index < threadCount; index++)
        {
            long start = index * chunkSize;
            long end = index == threadCount - 1
                ? totalBytes - 1
                : start + chunkSize - 1;

            segments.Add(new SegmentInfo
            {
                Index = index,
                RangeStart = start,
                RangeEnd = end,
                TempFilePath = Path.Combine(tempDirectory, $"seg_{index}.part"),
            });
        }

        return segments;
    }

    private static string GetStateFilePath(string tempDirectory) =>
        Path.Combine(tempDirectory, StateFileName);
}
