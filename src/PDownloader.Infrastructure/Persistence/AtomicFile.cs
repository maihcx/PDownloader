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

namespace PDownloader.Infrastructure.Persistence;

/// <summary>
/// Commits a flushed sibling file without truncating the last committed file.
/// Callers own writer serialization and must not use this for shared settings.
/// </summary>
public static class AtomicFile
{
    public static string GetBackupPath(string path) => path + ".bak";

    public static void WriteJson<T>(string path, T value, bool writeIndented = false,
        bool keepBackup = true) =>
        WriteAllText(path, JsonSerializer.Serialize(value,
            new JsonSerializerOptions { WriteIndented = writeIndented }), keepBackup);

    public static void WriteAllText(string path, string contents, bool keepBackup = true)
    {
        ArgumentNullException.ThrowIfNull(contents);
        string destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(contents));
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
            {
                File.Replace(temporary, destination, keepBackup ? GetBackupPath(destination) : null);
            }
            else
            {
                File.Move(temporary, destination); // Same directory/volume, first commit.
            }
        }
        finally
        {
            // Never fall back to delete/copy/truncate when replace fails.
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Debug.WriteLine($"[Persistence] Temporary file cleanup failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Validates primary then backup. Missing both means no saved state; corrupt
    /// state throws. Access/sharing errors do not authorize replacing live data.
    /// A valid backup is repaired into primary without rotating the corrupt file
    /// over the backup. Uncommitted temporary files are never recovery candidates.
    /// </summary>
    public static string? ReadAllText(string path, Action<string> validate,
        out bool recoveredFromBackup)
    {
        ArgumentNullException.ThrowIfNull(validate);
        recoveredFromBackup = false;
        Exception? primaryError = null;
        try
        {
            string? primary = ReadIfPresent(path);
            if (primary is not null)
            {
                validate(primary);
                return primary;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { primaryError = ex; }

        string? backup = ReadIfPresent(GetBackupPath(path));
        if (backup is null)
        {
            if (primaryError is not null)
            {
                throw new InvalidDataException($"Saved state is invalid: {path}", primaryError);
            }

            return null;
        }

        validate(backup);
        WriteAllText(path, backup, keepBackup: false);
        recoveredFromBackup = true;
        return backup;
    }

    private static string? ReadIfPresent(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
