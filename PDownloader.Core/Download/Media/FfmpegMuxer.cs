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

namespace PDownloader.Core.Download.Media;

internal sealed class FfmpegMuxer
{
    public async Task<string> MuxAsync(
        IReadOnlyList<DownloadedStreamFile> files,
        string outputFolder,
        string fileStem,
        Action<double>? reportProgress,
        FileMergeMode fileMergeMode,
        CancellationToken cancellationToken)
    {
        DownloadedStreamFile video = files.FirstOrDefault(file => file.Stream.HasVideo)
            ?? files[0];
        DownloadedStreamFile? audio = files.FirstOrDefault(
            file => file.Stream.HasAudio && !file.Stream.HasVideo);

        string videoExtension = (video.Stream.Ext ?? "mp4").ToLowerInvariant();
        string outputExtension = videoExtension is "mp4" or "webm" or "mkv"
            ? videoExtension
            : "mkv";
        string finalPath = DownloadPathService.UniqueFilePath(
            outputFolder,
            $"{fileStem}.{outputExtension}");

        var sourcePaths = new List<string> { video.Path };
        if (audio != null)
        {
            sourcePaths.Add(audio.Path);
        }

        ValidateSources(sourcePaths);

        if (fileMergeMode == FileMergeMode.HighPerformance)
        {
            return await ExecuteHighPerformanceAsync(
                sourcePaths,
                finalPath,
                reportProgress,
                cancellationToken);
        }

        string recoveryDirectory = MergeRecoveryStore.GetRecoveryDirectory(sourcePaths);
        var manifest = new MergeRecoveryManifest
        {
            Kind = MergeRecoveryKind.FfmpegMux,
            FileMergeMode = fileMergeMode,
            DestinationPath = finalPath,
            SourcePaths = sourcePaths,
            ExpectedOutputBytes = sourcePaths.Sum(GetFileLength),
            OutputLengthIsExact = false
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
        if (manifest.Kind != MergeRecoveryKind.FfmpegMux)
        {
            throw new InvalidOperationException(
                $"Invalid merge state: {manifest.Kind}.");
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
        if (File.Exists(manifest.DestinationPath)
            && new FileInfo(manifest.DestinationPath).Length > 0)
        {
            if (manifest.FileMergeMode == FileMergeMode.DataIntegrity)
            {
                await VerifyOutputDurabilityAsync(
                    manifest.DestinationPath,
                    cancellationToken);
            }

            reportProgress?.Invoke(100);
            CleanupSources(manifest.SourcePaths);
            return manifest.DestinationPath;
        }

        ValidateSources(manifest.SourcePaths);

        string ffmpegPath = FfmpegExecutableLocator.Instance.Find()
            ?? throw new InvalidOperationException(
                "ffmpeg not found — ffmpeg is required to merge separately downloaded video and audio into a single file. " +
                "Place ffmpeg.exe next to PDownloader.Core.exe or add it to the PATH.");

        string videoPath = manifest.SourcePaths[0];
        string? audioPath = manifest.SourcePaths.Count > 1
            ? manifest.SourcePaths[1]
            : null;
        string mergingPath = MergeRecoveryStore.GetPartialOutputPath(manifest);

        string? outputDirectory = Path.GetDirectoryName(manifest.DestinationPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        ProcessStartInfo startInfo = BuildStartInfo(
            ffmpegPath,
            videoPath,
            audioPath,
            mergingPath);

        long expectedOutputBytes = manifest.ExpectedOutputBytes > 0
            ? manifest.ExpectedOutputBytes
            : manifest.SourcePaths.Sum(GetFileLength);
        var mergeProgress = new MergeProgressTracker(
            expectedOutputBytes,
            reportProgress,
            maxProgressBeforeComplete: 99);
        mergeProgress.Start();

        using var process = new Process { StartInfo = startInfo };
        using var monitorCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            process.Start();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            Task progressMonitorTask = MonitorOutputProgressAsync(
                process,
                mergingPath,
                mergeProgress,
                monitorCancellation.Token);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }

                throw;
            }
            finally
            {
                monitorCancellation.Cancel();
                try
                {
                    await progressMonitorTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when the process finishes or the download is cancelled.
                }
            }

            string standardError = await standardErrorTask;
            if (process.ExitCode != 0
                || !File.Exists(mergingPath)
                || new FileInfo(mergingPath).Length <= 0)
            {
                string tail = standardError.Length > 500
                    ? standardError[^500..]
                    : standardError;
                throw new InvalidOperationException(
                    $"FFmpeg merging failed (exit {process.ExitCode}): {tail}");
            }

            if (manifest.FileMergeMode == FileMergeMode.DataIntegrity)
            {
                await VerifyOutputDurabilityAsync(mergingPath, cancellationToken);
            }

            if (File.Exists(manifest.DestinationPath))
            {
                throw new IOException(
                    $"Cannot complete file merging because the destination file already exists: " +
                    manifest.DestinationPath);
            }

            File.Move(mergingPath, manifest.DestinationPath);
            mergeProgress.Complete();

            CleanupSources(manifest.SourcePaths);
            return manifest.DestinationPath;
        }
        catch (OperationCanceledException)
        {
            TryDeletePartialOutput(mergingPath);

            reportProgress?.Invoke(0);
            throw;
        }
        catch (Exception ex)
        {
            TryDeletePartialOutput(mergingPath);
            throw new InvalidOperationException(
                "Video/audio merging failed. The downloaded streams have been retained. " +
                "Click Retry to re-run only the merge step. " +
                ex.Message,
                ex);
        }
    }

    private static async Task<string> ExecuteHighPerformanceAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        ValidateSources(sourcePaths);

        string ffmpegPath = FfmpegExecutableLocator.Instance.Find()
            ?? throw new InvalidOperationException(
                "ffmpeg not found — ffmpeg is required to merge separately downloaded video and audio into a single file. " +
                "Place ffmpeg.exe next to PDownloader.Core.exe or add it to the PATH.");

        if (File.Exists(destinationPath))
        {
            throw new IOException(
                $"Cannot complete file merging because the destination file already exists: {destinationPath}");
        }

        string? outputDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string videoPath = sourcePaths[0];
        string? audioPath = sourcePaths.Count > 1 ? sourcePaths[1] : null;
        ProcessStartInfo startInfo = BuildStartInfo(
            ffmpegPath,
            videoPath,
            audioPath,
            destinationPath);

        long expectedOutputBytes = sourcePaths.Sum(GetFileLength);
        var mergeProgress = new MergeProgressTracker(
            expectedOutputBytes,
            reportProgress,
            maxProgressBeforeComplete: 99);
        mergeProgress.Start();

        using var process = new Process { StartInfo = startInfo };
        using var monitorCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            process.Start();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            Task progressMonitorTask = MonitorOutputProgressAsync(
                process,
                destinationPath,
                mergeProgress,
                monitorCancellation.Token);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            finally
            {
                monitorCancellation.Cancel();
                try
                {
                    await progressMonitorTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when the process finishes or is cancelled.
                }
            }

            string standardError = await standardErrorTask;
            if (process.ExitCode != 0
                || !File.Exists(destinationPath)
                || new FileInfo(destinationPath).Length <= 0)
            {
                string tail = standardError.Length > 500
                    ? standardError[^500..]
                    : standardError;
                throw new InvalidOperationException(
                    $"FFmpeg merging failed (exit {process.ExitCode}): {tail}");
            }

            mergeProgress.Complete();
            CleanupSources(sourcePaths);
            return destinationPath;
        }
        catch
        {
            TryDeletePartialOutput(destinationPath);
            throw;
        }
    }

    private static async Task VerifyOutputDurabilityAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(
            outputPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        // A complete reread catches truncated or unreadable output before the source
        // streams are released. The hash is intentionally not persisted because the
        // normal completed-file hash pipeline remains the source of user-visible hashes.
        _ = await FileHashCalculator.ComputeAsync(outputPath, cancellationToken);
    }

    private static void ValidateSources(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0 || !File.Exists(sourcePaths[0]))
        {
            throw new InvalidOperationException(
                "Cannot retry the merging process due to the missing downloaded video stream.");
        }

        List<string> missing = sourcePaths
            .Where(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot retry the merging process due to {missing.Count} missing downloaded streams.");
        }
    }

    private static long GetFileLength(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static async Task MonitorOutputProgressAsync(
        Process process,
        string outputPath,
        MergeProgressTracker progress,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(outputPath))
            {
                try
                {
                    progress.SetProcessedBytes(new FileInfo(outputPath).Length);
                }
                catch (IOException)
                {
                    // The file may be between filesystem updates. Try again shortly.
                }
            }

            if (process.HasExited)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string ffmpegPath,
        string videoPath,
        string? audioPath,
        string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);

        if (!string.IsNullOrWhiteSpace(audioPath))
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(audioPath);
        }

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private static void CleanupSources(IEnumerable<string> sourcePaths)
    {
        foreach (string sourcePath in sourcePaths)
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
                    $"[MergeRecovery] Cannot delete the source stream '{sourcePath}': {ex.Message}");
            }
        }
    }

    private static void TryDeletePartialOutput(string mergingPath)
    {
        try
        {
            if (File.Exists(mergingPath))
            {
                File.Delete(mergingPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[MergeRecovery] Unable to delete the incomplete ffmpeg file '{mergingPath}': {ex.Message}");
        }
    }
}

internal sealed record DownloadedStreamFile(
    ResolvedStream Stream,
    string Path);
