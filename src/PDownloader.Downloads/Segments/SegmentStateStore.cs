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

using PDownloader.Infrastructure.Persistence;

namespace PDownloader.Downloads.Segments;

internal sealed class SegmentStateStore
{
    private const string StateFileName = "segments.pdstate";
    private readonly object _writeLock = new();

    public List<SegmentInfo> BuildOrRestore(string tempDirectory, long totalBytes, int threadCount)
    {
        Directory.CreateDirectory(tempDirectory);
        List<SegmentInfo> expected = BuildNew(tempDirectory, totalBytes, threadCount);
        List<SegmentInfo>? restored = TryRestore(tempDirectory, expected);
        if (restored is not null)
        {
            return restored;
        }

        Reset(tempDirectory);
        return expected;
    }

    public void Persist(string tempDirectory, IReadOnlyCollection<SegmentInfo> segments,
        bool keepBackup = true)
    {
        lock (_writeLock)
        {
            string path = GetStateFilePath(tempDirectory);
            // A destructive restart must invalidate the old backup before truncating
            // any part. Later checkpoints may safely rotate the reset primary.
            if (!keepBackup)
            {
                File.Delete(AtomicFile.GetBackupPath(path));
            }

            SegmentInfo[] snapshot = segments.Select(segment => segment.CaptureCheckpoint()).ToArray();
            AtomicFile.WriteJson(path, snapshot, keepBackup: keepBackup);
        }
        // A final/initial commit failure is observable; the monitor logs and retries
        // periodic failures. No caller may assume failed persistence succeeded.
    }

    public void Reset(string tempDirectory)
    {
        lock (_writeLock)
        {
            if (!Directory.Exists(tempDirectory))
            {
                return;
            }

            string path = GetStateFilePath(tempDirectory);
            // Remove recovery metadata first, then its data. Never reuse a backup
            // belonging to a previous layout after a range fallback or reset.
            File.Delete(AtomicFile.GetBackupPath(path));
            File.Delete(path);
            foreach (string part in Directory.EnumerateFiles(tempDirectory, "seg_*.part"))
            {
                File.Delete(part);
            }
        }
    }

    private static List<SegmentInfo>? TryRestore(string tempDirectory,
        IReadOnlyList<SegmentInfo> expected)
    {
        List<SegmentInfo> saved;
        try
        {
            string? json = AtomicFile.ReadAllText(GetStateFilePath(tempDirectory),
                value => { ParseAndValidate(value, expected); }, out bool recovered);
            if (json is null)
            {
                return null;
            }

            saved = ParseAndValidate(json, expected);
            Debug.WriteLine($"[Segments] Restored checkpoint; backup={recovered}.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            Debug.WriteLine($"[Segments] Invalid checkpoint; restarting segments: {ex.Message}");
            return null;
        }
        // Sharing/access/I/O errors propagate without deleting recoverable files.
        foreach (SegmentInfo segment in saved)
        {
            long actualLength;
            bool exists = true;
            try { actualLength = new FileInfo(segment.TempFilePath).Length; }
            catch (FileNotFoundException) { actualLength = 0; exists = false; }
            catch (DirectoryNotFoundException) { actualLength = 0; exists = false; }

            long committedLength = Math.Min(actualLength, segment.BytesWritten);
            bool completed = exists && segment.IsCompleted && actualLength >= segment.BytesWritten;
            if (segment.RangeEnd >= 0)
            {
                completed = committedLength == segment.Length;
            }

            if (actualLength > committedLength)
            {
                // Discard bytes written after the last durable checkpoint. File
                // length alone must never promote an uncommitted tail to progress.
                using var part = new FileStream(segment.TempFilePath, FileMode.Open,
                    FileAccess.Write, FileShare.None);
                part.SetLength(committedLength);
                part.Flush(flushToDisk: true);
            }

            segment.BytesWritten = committedLength;
            segment.IsCompleted = completed;
            segment.CommitCheckpoint(committedLength, completed);
            segment.TransferState = completed ? DownloadThreadState.Completed : DownloadThreadState.Waiting;
            segment.RetryAttempt = 0;
        }

        return saved;
    }

    private static List<SegmentInfo> ParseAndValidate(string json,
        IReadOnlyList<SegmentInfo> expected)
    {
        List<SegmentInfo> saved = JsonSerializer.Deserialize<List<SegmentInfo>>(json)
            ?? throw new InvalidDataException("Segment state must contain an array.");
        if (saved.Count != expected.Count)
        {
            throw new InvalidDataException("Segment layout changed.");
        }

        for (int index = 0; index < saved.Count; index++)
        {
            SegmentInfo segment = saved[index];
            SegmentInfo range = expected[index];
            if (segment is null || segment.Index != range.Index
                || segment.RangeStart != range.RangeStart || segment.RangeEnd != range.RangeEnd
                || segment.BytesWritten < 0
                || (range.RangeEnd >= 0 && segment.BytesWritten > range.Length)
                || string.IsNullOrWhiteSpace(segment.TempFilePath))
            {
                throw new InvalidDataException("Invalid segment range or checkpoint.");
            }

            try
            {
                if (!Path.GetFullPath(segment.TempFilePath).Equals(
                    Path.GetFullPath(range.TempFilePath), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Segment path does not belong to this download.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { throw new InvalidDataException("Invalid segment path.", ex); }
        }

        return saved;
    }

    private static List<SegmentInfo> BuildNew(string tempDirectory, long totalBytes, int threadCount)
    {
        threadCount = totalBytes > 0 ? (int)Math.Min(Math.Max(1, threadCount), totalBytes) : 1;
        var segments = new List<SegmentInfo>(threadCount);
        long chunkSize = totalBytes > 0 ? totalBytes / threadCount : 0;
        for (int index = 0; index < threadCount; index++)
        {
            long start = index * chunkSize;
            long end = totalBytes <= 0 ? -1 : index == threadCount - 1
                ? totalBytes - 1 : start + chunkSize - 1;
            segments.Add(new SegmentInfo
            {
                Index = index,
                RangeStart = start,
                RangeEnd = end,
                TempFilePath = Path.Combine(tempDirectory, $"seg_{index}.part")
            });
        }

        return segments;
    }

    private static string GetStateFilePath(string tempDirectory) =>
        Path.Combine(tempDirectory, StateFileName);
}
