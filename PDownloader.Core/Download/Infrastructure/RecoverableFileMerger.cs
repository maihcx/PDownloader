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

internal sealed class RecoverableFileMerger
{
    public async Task<string> MergeAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        ValidateAllSources(sourcePaths);

        List<long> sourceLengths = sourcePaths
            .Select(path => new FileInfo(path).Length)
            .ToList();
        string recoveryDirectory = MergeRecoveryStore.GetRecoveryDirectory(sourcePaths);
        long expectedOutputBytes = sourceLengths.Sum();

        var manifest = new MergeRecoveryManifest
        {
            Version = 2,
            Kind = MergeRecoveryKind.Concatenate,
            DestinationPath = destinationPath,
            SourcePaths = sourcePaths.ToList(),
            SourceLengths = sourceLengths,
            ExpectedOutputBytes = expectedOutputBytes,
            OutputLengthIsExact = true,
            NextSourceIndex = 0,
            CommittedOutputBytes = 0
        };

        MergeRecoveryStore.Save(recoveryDirectory, manifest);
        return await ExecuteAsync(
            manifest,
            reportProgress,
            cancellationToken);
    }

    public Task<string> RetryAsync(
        MergeRecoveryManifest manifest,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (manifest.Kind != MergeRecoveryKind.Concatenate)
        {
            throw new InvalidOperationException(
                $"Trạng thái merge không hợp lệ: {manifest.Kind}.");
        }

        return ExecuteAsync(
            manifest,
            reportProgress,
            cancellationToken);
    }

    private static async Task<string> ExecuteAsync(
        MergeRecoveryManifest manifest,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        string destinationPath = manifest.DestinationPath;
        string mergingPath = MergeRecoveryStore.GetPartialOutputPath(manifest);
        string recoveryDirectory = MergeRecoveryStore.GetRecoveryDirectory(manifest.SourcePaths);

        if (IsAlreadyFinalized(manifest))
        {
            reportProgress?.Invoke(100);
            CleanupSources(manifest.SourcePaths);
            return destinationPath;
        }

        UpgradeLegacyManifestIfNeeded(recoveryDirectory, manifest);
        RecoverCheckpointFloorFromDeletedSources(
            recoveryDirectory,
            manifest,
            mergingPath);
        ValidateCheckpoint(manifest);
        PreparePartialOutputForResume(manifest, mergingPath, recoveryDirectory);
        ValidateRemainingSources(manifest);
        CleanupCommittedSourceFiles(manifest);

        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var mergeProgress = new MergeProgressTracker(
            manifest.ExpectedOutputBytes,
            reportProgress);
        mergeProgress.Start(manifest.CommittedOutputBytes);

        try
        {
            await using (var output = new FileStream(
                mergingPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None))
            {
                output.SetLength(manifest.CommittedOutputBytes);
                output.Position = manifest.CommittedOutputBytes;

                for (int index = manifest.NextSourceIndex;
                     index < manifest.SourcePaths.Count;
                     index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string sourcePath = manifest.SourcePaths[index];
                    long expectedSourceLength = manifest.SourceLengths[index];
                    ValidateSourceForMerge(sourcePath, expectedSourceLength, index);

                    await using (var input = new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                    {
                        await mergeProgress.CopyToAsync(
                            input,
                            output,
                            cancellationToken);
                    }

                    long previousCommittedLength = manifest.CommittedOutputBytes;
                    int previousNextSourceIndex = manifest.NextSourceIndex;
                    long expectedCommittedLength = checked(
                        previousCommittedLength + expectedSourceLength);

                    if (output.Position != expectedCommittedLength)
                    {
                        throw new IOException(
                            $"Kích thước dữ liệu sau khi ghép segment {index} không hợp lệ: " +
                            $"{output.Position} B, dự kiến {expectedCommittedLength} B.");
                    }

                    // Commit protocol:
                    // 1. Flush all copied bytes to the physical disk.
                    // 2. Atomically persist the checkpoint.
                    // 3. Only then delete the source that is now recoverable from .merging.
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);

                    manifest.CommittedOutputBytes = expectedCommittedLength;
                    manifest.NextSourceIndex = index + 1;

                    try
                    {
                        MergeRecoveryStore.SaveCheckpoint(recoveryDirectory, manifest);
                    }
                    catch
                    {
                        // The source has not been deleted yet. Restore the in-memory
                        // checkpoint so the outer rollback also returns to the last
                        // checkpoint that was actually persisted.
                        manifest.CommittedOutputBytes = previousCommittedLength;
                        manifest.NextSourceIndex = previousNextSourceIndex;
                        throw;
                    }

                    TryDeleteCommittedSource(sourcePath);
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);

                long mergedLength = output.Length;
                if (manifest.OutputLengthIsExact
                    && manifest.ExpectedOutputBytes >= 0
                    && mergedLength != manifest.ExpectedOutputBytes)
                {
                    throw new IOException(
                        $"Kích thước file sau khi ghép không hợp lệ: " +
                        $"{mergedLength} B, dự kiến {manifest.ExpectedOutputBytes} B.");
                }
            }

            if (File.Exists(destinationPath))
            {
                throw new IOException(
                    $"Không thể hoàn tất ghép file vì file đích đã tồn tại: {destinationPath}");
            }

            File.Move(mergingPath, destinationPath);
            mergeProgress.Complete();

            CleanupSources(manifest.SourcePaths);
            return destinationPath;
        }
        catch (OperationCanceledException)
        {
            RollBackUncommittedBytes(mergingPath, manifest.CommittedOutputBytes);
            throw;
        }
        catch (Exception ex)
        {
            RollBackUncommittedBytes(mergingPath, manifest.CommittedOutputBytes);
            throw new InvalidOperationException(
                "Ghép file thất bại. Các phần đã commit vẫn được giữ trong file .merging " +
                "và các segment chưa commit vẫn còn nguyên. Nhấn Thử lại để tiếp tục ghép " +
                "từ checkpoint gần nhất. " + ex.Message,
                ex);
        }
    }

    private static void UpgradeLegacyManifestIfNeeded(
        string recoveryDirectory,
        MergeRecoveryManifest manifest)
    {
        bool requiresUpgrade = manifest.Version < 2
            || manifest.SourceLengths.Count != manifest.SourcePaths.Count;
        if (!requiresUpgrade)
        {
            return;
        }

        // Version 1 never deleted sources before the whole merge completed, so it is
        // safe to rebuild the per-source length table and start checkpointing from 0.
        ValidateAllSources(manifest.SourcePaths);

        manifest.Version = 2;
        manifest.SourceLengths = manifest.SourcePaths
            .Select(path => new FileInfo(path).Length)
            .ToList();
        manifest.ExpectedOutputBytes = manifest.SourceLengths.Sum();
        manifest.NextSourceIndex = 0;
        manifest.CommittedOutputBytes = 0;

        MergeRecoveryStore.Save(recoveryDirectory, manifest);
    }

    private static void RecoverCheckpointFloorFromDeletedSources(
        string recoveryDirectory,
        MergeRecoveryManifest manifest,
        string mergingPath)
    {
        int highestMissingSourceIndex = -1;
        for (int index = 0; index < manifest.SourcePaths.Count; index++)
        {
            if (!File.Exists(manifest.SourcePaths[index]))
            {
                highestMissingSourceIndex = index;
            }
        }

        int minimumCommittedSourceCount = highestMissingSourceIndex + 1;
        if (manifest.NextSourceIndex >= minimumCommittedSourceCount)
        {
            return;
        }

        long minimumCommittedBytes = manifest.SourceLengths
            .Take(minimumCommittedSourceCount)
            .Sum();

        if (!File.Exists(mergingPath)
            || new FileInfo(mergingPath).Length < minimumCommittedBytes)
        {
            throw new InvalidOperationException(
                "Checkpoint merge bị thiếu hoặc cũ, và file .merging không còn đủ dữ liệu " +
                "để khôi phục các segment nguồn đã được giải phóng.");
        }

        manifest.NextSourceIndex = minimumCommittedSourceCount;
        manifest.CommittedOutputBytes = minimumCommittedBytes;
        MergeRecoveryStore.SaveCheckpoint(recoveryDirectory, manifest);
    }

    private static void ValidateCheckpoint(MergeRecoveryManifest manifest)
    {
        if (manifest.SourcePaths.Count == 0)
        {
            throw new InvalidOperationException("Không có dữ liệu nguồn để ghép file.");
        }

        if (manifest.SourceLengths.Count != manifest.SourcePaths.Count)
        {
            throw new InvalidOperationException(
                "Trạng thái phục hồi merge không hợp lệ: số lượng kích thước source không khớp.");
        }

        if (manifest.NextSourceIndex < 0
            || manifest.NextSourceIndex > manifest.SourcePaths.Count)
        {
            throw new InvalidOperationException(
                "Trạng thái phục hồi merge không hợp lệ: checkpoint source vượt phạm vi.");
        }

        long expectedCommittedBytes = manifest.SourceLengths
            .Take(manifest.NextSourceIndex)
            .Sum();
        if (manifest.CommittedOutputBytes != expectedCommittedBytes)
        {
            throw new InvalidOperationException(
                "Trạng thái phục hồi merge không hợp lệ: kích thước checkpoint không khớp " +
                $"({manifest.CommittedOutputBytes} B != {expectedCommittedBytes} B).");
        }

        if (manifest.ExpectedOutputBytes != manifest.SourceLengths.Sum())
        {
            throw new InvalidOperationException(
                "Trạng thái phục hồi merge không hợp lệ: tổng kích thước nguồn không khớp.");
        }
    }

    private static void PreparePartialOutputForResume(
        MergeRecoveryManifest manifest,
        string mergingPath,
        string recoveryDirectory)
    {
        if (manifest.CommittedOutputBytes == 0)
        {
            RollBackUncommittedBytes(mergingPath, 0);
            return;
        }

        if (!File.Exists(mergingPath))
        {
            if (CanRestartFromSources(manifest))
            {
                ResetCheckpoint(recoveryDirectory, manifest);
                return;
            }

            throw new InvalidOperationException(
                "Không thể tiếp tục merge: file .merging chứa dữ liệu đã commit không còn tồn tại, " +
                "trong khi một số segment nguồn đã được giải phóng để tiết kiệm dung lượng.");
        }

        long partialLength = new FileInfo(mergingPath).Length;
        if (partialLength < manifest.CommittedOutputBytes)
        {
            if (CanRestartFromSources(manifest))
            {
                RollBackUncommittedBytes(mergingPath, 0);
                ResetCheckpoint(recoveryDirectory, manifest);
                return;
            }

            throw new InvalidOperationException(
                "Không thể tiếp tục merge: file .merging ngắn hơn checkpoint đã commit " +
                $"({partialLength} B < {manifest.CommittedOutputBytes} B). Dữ liệu nguồn đã commit " +
                "không còn đầy đủ để xây dựng lại từ đầu.");
        }

        RollBackUncommittedBytes(mergingPath, manifest.CommittedOutputBytes);
    }

    private static bool CanRestartFromSources(MergeRecoveryManifest manifest) =>
        manifest.SourcePaths.All(File.Exists);

    private static void ResetCheckpoint(
        string recoveryDirectory,
        MergeRecoveryManifest manifest)
    {
        manifest.NextSourceIndex = 0;
        manifest.CommittedOutputBytes = 0;
        MergeRecoveryStore.SaveCheckpoint(recoveryDirectory, manifest);
    }

    private static void ValidateRemainingSources(MergeRecoveryManifest manifest)
    {
        for (int index = manifest.NextSourceIndex;
             index < manifest.SourcePaths.Count;
             index++)
        {
            ValidateSourceForMerge(
                manifest.SourcePaths[index],
                manifest.SourceLengths[index],
                index);
        }
    }

    private static void ValidateAllSources(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0)
        {
            throw new InvalidOperationException("Không có dữ liệu nguồn để ghép file.");
        }

        List<string> missing = sourcePaths
            .Where(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Không thể thử lại quá trình ghép vì thiếu {missing.Count} file dữ liệu tạm.");
        }
    }

    private static void ValidateSourceForMerge(
        string sourcePath,
        long expectedLength,
        int index)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException(
                $"Không thể tiếp tục merge vì thiếu segment chưa commit ở index {index}.");
        }

        long actualLength = new FileInfo(sourcePath).Length;
        if (actualLength != expectedLength)
        {
            throw new IOException(
                $"Segment {index} có kích thước không hợp lệ: " +
                $"{actualLength} B, dự kiến {expectedLength} B.");
        }
    }

    private static bool IsAlreadyFinalized(MergeRecoveryManifest manifest)
    {
        if (!File.Exists(manifest.DestinationPath))
        {
            return false;
        }

        if (!manifest.OutputLengthIsExact)
        {
            return new FileInfo(manifest.DestinationPath).Length > 0;
        }

        return new FileInfo(manifest.DestinationPath).Length
            == manifest.ExpectedOutputBytes;
    }

    private static void CleanupCommittedSourceFiles(MergeRecoveryManifest manifest)
    {
        for (int index = 0; index < manifest.NextSourceIndex; index++)
        {
            TryDeleteCommittedSource(manifest.SourcePaths[index]);
        }
    }

    private static void CleanupSources(IEnumerable<string> sourcePaths)
    {
        foreach (string sourcePath in sourcePaths)
        {
            TryDeleteCommittedSource(sourcePath);
        }
    }

    private static void TryDeleteCommittedSource(string sourcePath)
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
                $"[MergeRecovery] Không thể xóa dữ liệu nguồn '{sourcePath}': {ex.Message}");
        }
    }

    private static void RollBackUncommittedBytes(
        string mergingPath,
        long committedLength)
    {
        try
        {
            if (!File.Exists(mergingPath))
            {
                return;
            }

            using var stream = new FileStream(
                mergingPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None);

            if (stream.Length != committedLength)
            {
                stream.SetLength(committedLength);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[MergeRecovery] Không thể rollback file merge dở '{mergingPath}' " +
                $"về {committedLength} B: {ex.Message}");
        }
    }
}
