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
        CancellationToken cancellationToken)
    {
        string ffmpegPath = FfmpegExecutableLocator.Instance.Find()
            ?? throw new InvalidOperationException(
                "ffmpeg không tìm thấy — cần ffmpeg để ghép video+audio tải riêng thành 1 file. " +
                "Đặt ffmpeg.exe cạnh PDownloader.Core.exe hoặc thêm vào PATH.");

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

        ProcessStartInfo startInfo = BuildStartInfo(
            ffmpegPath,
            video.Path,
            audio?.Path,
            finalPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        string standardError = await standardErrorTask;
        if (process.ExitCode != 0 || !File.Exists(finalPath))
        {
            string tail = standardError.Length > 500
                ? standardError[^500..]
                : standardError;
            throw new InvalidOperationException(
                $"ffmpeg ghép thất bại (exit {process.ExitCode}): {tail}");
        }

        foreach (DownloadedStreamFile file in files)
        {
            try { File.Delete(file.Path); } catch { }
        }

        return finalPath;
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
}

internal sealed record DownloadedStreamFile(
    ResolvedStream Stream,
    string Path);
