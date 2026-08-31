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

namespace PDownloader.Infrastructure.Downloads;

internal enum MergeRecoveryKind
{
    Concatenate,
    FfmpegMux
}

internal sealed class MergeRecoveryManifest
{
    public int Version { get; set; } = 2;

    public MergeRecoveryKind Kind { get; set; }

    public FileMergeMode FileMergeMode { get; set; } = FileMergeMode.Balanced;

    public string DestinationPath { get; set; } = string.Empty;

    public List<string> SourcePaths { get; set; } = new();

    public List<long> SourceLengths { get; set; } = new();

    public long ExpectedOutputBytes { get; set; }

    public bool OutputLengthIsExact { get; set; }

    public int NextSourceIndex { get; set; }

    public long CommittedOutputBytes { get; set; }

    public string Md5Hash { get; set; } = string.Empty;

    public string Sha1Hash { get; set; } = string.Empty;

    public string Sha256Hash { get; set; } = string.Empty;
}

internal sealed class MergeRecoveryCheckpoint
{
    public int Version { get; set; } = 1;

    public int NextSourceIndex { get; set; }

    public long CommittedOutputBytes { get; set; }
}

internal static class MergeRecoveryStore
{
    private const string StateFileName = "merge-recovery.pdstate";
    private const string CheckpointFileName = "merge-recovery.checkpoint";

    public static string GetStateFilePath(string recoveryDirectory) =>
        Path.Combine(recoveryDirectory, StateFileName);

    public static string GetCheckpointFilePath(string recoveryDirectory) =>
        Path.Combine(recoveryDirectory, CheckpointFileName);

    public static bool HasPending(string recoveryDirectory) =>
        TryLoad(recoveryDirectory) != null;

    public static bool HasPendingInTree(string recoveryDirectory)
    {
        if (HasPending(recoveryDirectory))
        {
            return true;
        }

        if (!Directory.Exists(recoveryDirectory))
        {
            return false;
        }

        try
        {
            return Directory
                .EnumerateFiles(
                    recoveryDirectory,
                    StateFileName,
                    SearchOption.AllDirectories)
                .Any(statePath =>
                {
                    string? directory = Path.GetDirectoryName(statePath);
                    return !string.IsNullOrWhiteSpace(directory)
                        && TryLoad(directory) != null;
                });
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[MergeRecovery] Unable to scan merge status: {ex.Message}");
            return false;
        }
    }

    public static bool TryGetPendingProgressInTree(
        string recoveryDirectory,
        out double progress)
    {
        progress = 0;

        MergeRecoveryManifest? rootManifest = TryLoad(recoveryDirectory);
        if (rootManifest != null)
        {
            progress = GetRecoverableProgress(rootManifest);
            return true;
        }

        if (!Directory.Exists(recoveryDirectory))
        {
            return false;
        }

        try
        {
            IEnumerable<string> recoveryDirectories = Directory
                .EnumerateFiles(
                    recoveryDirectory,
                    StateFileName,
                    SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(GetRecoveryStateTimestampUtc);

            foreach (string directory in recoveryDirectories)
            {
                MergeRecoveryManifest? manifest = TryLoad(directory);
                if (manifest == null)
                {
                    continue;
                }

                progress = GetRecoverableProgress(manifest);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[MergeRecovery] Unable to read the pending merge process: {ex.Message}");
        }

        return false;
    }

    private static double GetRecoverableProgress(MergeRecoveryManifest manifest)
    {
        try
        {
            if (File.Exists(manifest.DestinationPath)
                && new FileInfo(manifest.DestinationPath).Length > 0)
            {
                return 100;
            }
        }
        catch
        {
            // Fall back to the durable checkpoint below.
        }

        if (manifest.Kind == MergeRecoveryKind.Concatenate
            && manifest.ExpectedOutputBytes > 0)
        {
            return Math.Clamp(
                manifest.CommittedOutputBytes
                    / (double)manifest.ExpectedOutputBytes * 100.0,
                0,
                100);
        }

        return 0;
    }

    private static DateTime GetRecoveryStateTimestampUtc(string recoveryDirectory)
    {
        DateTime stateTimestamp = GetLastWriteTimeUtcSafe(
            GetStateFilePath(recoveryDirectory));
        DateTime checkpointTimestamp = GetLastWriteTimeUtcSafe(
            GetCheckpointFilePath(recoveryDirectory));

        return stateTimestamp >= checkpointTimestamp
            ? stateTimestamp
            : checkpointTimestamp;
    }

    private static DateTime GetLastWriteTimeUtcSafe(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    public static void Save(
        string recoveryDirectory,
        MergeRecoveryManifest manifest)
    {
        Directory.CreateDirectory(recoveryDirectory);
        WriteJsonAtomically(
            GetStateFilePath(recoveryDirectory),
            manifest,
            writeIndented: true);

        if (manifest.Kind == MergeRecoveryKind.Concatenate
            && manifest.Version >= 2)
        {
            SaveCheckpoint(recoveryDirectory, manifest);
        }
    }

    public static void SaveCheckpoint(
        string recoveryDirectory,
        MergeRecoveryManifest manifest)
    {
        Directory.CreateDirectory(recoveryDirectory);

        var checkpoint = new MergeRecoveryCheckpoint
        {
            NextSourceIndex = manifest.NextSourceIndex,
            CommittedOutputBytes = manifest.CommittedOutputBytes
        };

        WriteJsonAtomically(
            GetCheckpointFilePath(recoveryDirectory),
            checkpoint,
            writeIndented: false);
    }

    public static MergeRecoveryManifest? TryLoad(string recoveryDirectory)
    {
        string statePath = GetStateFilePath(recoveryDirectory);
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            MergeRecoveryManifest? manifest = JsonSerializer.Deserialize<MergeRecoveryManifest>(
                File.ReadAllText(statePath));

            if (manifest == null
                || manifest.Version is < 1 or > 2
                || string.IsNullOrWhiteSpace(manifest.DestinationPath)
                || manifest.SourcePaths.Count == 0)
            {
                return null;
            }

            if (manifest.Kind == MergeRecoveryKind.FfmpegMux
                && !File.Exists(manifest.DestinationPath)
                && manifest.SourcePaths.Any(path =>
                    string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
            {
                TryDeleteInvalidRecoveryState(recoveryDirectory);
                return null;
            }

            if (manifest.Kind == MergeRecoveryKind.Concatenate
                && manifest.Version >= 2)
            {
                ApplyCheckpointIfAvailable(recoveryDirectory, manifest);
            }

            return manifest;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MergeRecovery] Unable to read merge status: {ex.Message}");
            return null;
        }
    }

    public static string GetPartialOutputPath(MergeRecoveryManifest manifest)
    {
        if (manifest.Kind == MergeRecoveryKind.FfmpegMux)
        {
            string? directory = Path.GetDirectoryName(manifest.DestinationPath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(
                manifest.DestinationPath);
            string extension = Path.GetExtension(manifest.DestinationPath);
            return Path.Combine(
                directory ?? string.Empty,
                $"{fileNameWithoutExtension}.merging{extension}");
        }

        return manifest.DestinationPath + ".merging";
    }

    public static string GetRecoveryDirectory(IReadOnlyList<string> sourcePaths)
    {
        string? firstSource = sourcePaths.FirstOrDefault(
            path => !string.IsNullOrWhiteSpace(path));
        string? directory = firstSource == null
            ? null
            : Path.GetDirectoryName(firstSource);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Unable to determine the temporary directory for saving the file merging state.");
        }

        return directory;
    }
    private static void ApplyCheckpointIfAvailable(
        string recoveryDirectory,
        MergeRecoveryManifest manifest)
    {
        string checkpointPath = GetCheckpointFilePath(recoveryDirectory);
        if (!File.Exists(checkpointPath))
        {
            return;
        }

        try
        {
            MergeRecoveryCheckpoint? checkpoint =
                JsonSerializer.Deserialize<MergeRecoveryCheckpoint>(
                    File.ReadAllText(checkpointPath));

            if (checkpoint == null || checkpoint.Version != 1)
            {
                return;
            }

            manifest.NextSourceIndex = checkpoint.NextSourceIndex;
            manifest.CommittedOutputBytes = checkpoint.CommittedOutputBytes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[MergeRecovery] Unable to read checkpoint merge: {ex.Message}");
        }
    }

    private static void WriteJsonAtomically<T>(
        string destinationPath,
        T value,
        bool writeIndented)
    {
        // Merge checkpoints cannot roll back after source files have been deleted.
        AtomicFile.WriteJson(destinationPath, value, writeIndented, keepBackup: false);
    }

    private static void TryDeleteInvalidRecoveryState(string recoveryDirectory)
    {
        TryDeleteTemporaryState(GetStateFilePath(recoveryDirectory));
        TryDeleteTemporaryState(GetCheckpointFilePath(recoveryDirectory));
    }

    private static void TryDeleteTemporaryState(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
