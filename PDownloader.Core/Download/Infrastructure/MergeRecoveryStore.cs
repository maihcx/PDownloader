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

internal enum MergeRecoveryKind
{
    Concatenate,
    FfmpegMux
}

internal sealed class MergeRecoveryManifest
{
    public int Version { get; set; } = 2;

    public MergeRecoveryKind Kind { get; set; }

    public string DestinationPath { get; set; } = string.Empty;

    public List<string> SourcePaths { get; set; } = new();

    public List<long> SourceLengths { get; set; } = new();

    public long ExpectedOutputBytes { get; set; }

    public bool OutputLengthIsExact { get; set; }

    /// <summary>
    /// Index of the first source that has not been durably committed to the
    /// partial output yet. Sources before this index may already be deleted.
    /// </summary>
    public int NextSourceIndex { get; set; }

    /// <summary>
    /// Number of bytes that have been flushed to disk and recorded in the
    /// checkpoint. The partial output is truncated back to this length before
    /// a retry resumes.
    /// </summary>
    public long CommittedOutputBytes { get; set; }
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
                $"[MergeRecovery] Không thể quét trạng thái merge: {ex.Message}");
            return false;
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

        // Checkpoints are intentionally stored separately from the full manifest.
        // HLS downloads may contain thousands of fragment paths, so rewriting the
        // complete manifest after every committed fragment would cause avoidable I/O.
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

            if (manifest.Kind == MergeRecoveryKind.Concatenate
                && manifest.Version >= 2)
            {
                ApplyCheckpointIfAvailable(recoveryDirectory, manifest);
            }

            return manifest;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MergeRecovery] Không thể đọc trạng thái merge: {ex.Message}");
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
                "Không xác định được thư mục tạm để lưu trạng thái ghép file.");
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
                $"[MergeRecovery] Không thể đọc checkpoint merge: {ex.Message}");
        }
    }

    private static void WriteJsonAtomically<T>(
        string destinationPath,
        T value,
        bool writeIndented)
    {
        string temporaryPath = destinationPath + ".tmp";
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            value,
            new JsonSerializerOptions { WriteIndented = writeIndented });

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryState(temporaryPath);
            throw;
        }
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
            // Best effort cleanup only. The committed state is never touched.
        }
    }

}
