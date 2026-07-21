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

internal sealed class SegmentMerger
{
    public async Task MergeAsync(
        IReadOnlyCollection<SegmentInfo> segments,
        string destinationPath,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        ValidateSegments(segments);

        string? directory = Path.GetDirectoryName(destinationPath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        string mergingPath = destinationPath + ".merging";
        long totalBytes = segments.Sum(segment =>
            new FileInfo(segment.TempFilePath).Length);
        var mergeProgress = new MergeProgressTracker(totalBytes, reportProgress);
        mergeProgress.Start();

        try
        {
            await using (var output = new FileStream(
                mergingPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                foreach (SegmentInfo segment in segments.OrderBy(segment => segment.Index))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await using (var input = new FileStream(
                        segment.TempFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                    {
                        await mergeProgress.CopyToAsync(
                            input,
                            output,
                            cancellationToken);
                    }

                    TryDelete(segment.TempFilePath, segment.Index);
                }
            }

            File.Move(mergingPath, destinationPath, overwrite: true);
            mergeProgress.Complete();
        }
        catch
        {
            TryDelete(mergingPath, segmentIndex: null);
            throw;
        }
    }

    private static void ValidateSegments(IReadOnlyCollection<SegmentInfo> segments)
    {
        List<SegmentInfo> incomplete = segments.Where(segment => !segment.IsCompleted).ToList();
        if (incomplete.Count > 0)
        {
            string indexes = string.Join(", ", incomplete.Select(segment => segment.Index));
            throw new InvalidOperationException(
                $"Tải chưa hoàn tất: {incomplete.Count} segment chưa xong " +
                $"(index: {indexes}).");
        }

        List<SegmentInfo> missing = segments
            .Where(segment => !File.Exists(segment.TempFilePath))
            .ToList();
        if (missing.Count > 0)
        {
            string indexes = string.Join(", ", missing.Select(segment => segment.Index));
            throw new InvalidOperationException(
                $"Không thể ghép file: thiếu {missing.Count} segment " +
                $"(index: {indexes}).");
        }
    }

    private static void TryDelete(string path, int? segmentIndex)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            string target = segmentIndex.HasValue
                ? $"segment {segmentIndex.Value}"
                : "file .merging";
            Debug.WriteLine($"[Merge] Không thể xóa {target}: {ex.Message}");
        }
    }
}
