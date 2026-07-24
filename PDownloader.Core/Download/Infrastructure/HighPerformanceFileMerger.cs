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

namespace PDownloader.Core.Download.Infrastructure;

/// <summary>
/// Fast, non-recoverable concatenation path. It intentionally avoids durable
/// checkpoints, per-part disk flushes, and inline hashing. Source parts are
/// released as soon as they have been copied, so an interrupted merge may not
/// be recoverable.
/// </summary>
internal sealed class HighPerformanceFileMerger
{
    public async Task<string> MergeAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        ValidateSources(sourcePaths);

        if (File.Exists(destinationPath))
        {
            throw new IOException(
                $"Cannot complete file merging because the destination file already exists: {destinationPath}");
        }

        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        long expectedOutputBytes = sourcePaths.Sum(path => new FileInfo(path).Length);
        var progress = new MergeProgressTracker(expectedOutputBytes, reportProgress);
        progress.Start();

        try
        {
            await using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            foreach (string sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using (var input = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1024 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await progress.CopyToAsync(
                        input,
                        output,
                        onBytesWritten: null,
                        cancellationToken);
                }

                // Deliberately no durable checkpoint or Flush(flushToDisk: true).
                TryDeleteSource(sourcePath);
            }

            await output.FlushAsync(cancellationToken);

            if (output.Length != expectedOutputBytes)
            {
                throw new IOException(
                    $"Invalid file size after high-performance merge: {output.Length} B, " +
                    $"expected {expectedOutputBytes} B.");
            }

            progress.Complete();
            return destinationPath;
        }
        catch
        {
            TryDeletePartialOutput(destinationPath);
            throw;
        }
    }

    private static void ValidateSources(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0)
        {
            throw new InvalidOperationException("No source data available to merge files.");
        }

        List<string> missing = sourcePaths
            .Where(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot merge files because {missing.Count} temporary data files are missing.");
        }
    }

    private static void TryDeleteSource(string sourcePath)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[HighPerformanceMerge] Cannot release source '{sourcePath}': {ex.Message}");
        }
    }

    private static void TryDeletePartialOutput(string destinationPath)
    {
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[HighPerformanceMerge] Cannot delete partial output '{destinationPath}': {ex.Message}");
        }
    }
}
