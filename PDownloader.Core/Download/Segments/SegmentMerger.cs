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
    private readonly RecoverableFileMerger _fileMerger = new();

    public async Task MergeAsync(
        IReadOnlyCollection<SegmentInfo> segments,
        string destinationPath,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        ValidateSegments(segments);

        List<string> sourcePaths = segments
            .OrderBy(segment => segment.Index)
            .Select(segment => segment.TempFilePath)
            .ToList();

        await _fileMerger.MergeAsync(
            sourcePaths,
            destinationPath,
            reportProgress,
            cancellationToken);
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
}
