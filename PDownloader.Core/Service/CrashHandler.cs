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

using System.Runtime.InteropServices;

namespace PDownloader.Core.Services;

public static class CrashHandler
{
    private static int _crashHandled = 0;

    private const string BugTrackerExe = "PDownloader.BugTracker.exe";

    public static void Handle(Exception ex, string source)
    {
        if (Interlocked.CompareExchange(ref _crashHandled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            string logPath = WriteCrashLog(ex, source);
            LaunchBugTracker(logPath);
        }
        catch { }
        finally
        {
            Environment.Exit(1);
        }
    }

    public static void WriteOnly(Exception ex, string source)
    {
        try { WriteCrashLog(ex, source); }
        catch { }
    }

    private static string WriteCrashLog(Exception ex, string source)
    {
        string logDir = Path.Combine(Path.GetTempPath(), "PDownloader");
        string logPath = Path.Combine(logDir,
            $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        Directory.CreateDirectory(logDir);

        var sb = new StringBuilder();
        sb.AppendLine($"PDownloader Crash Report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Source  : {source}");
        sb.AppendLine($"OS      : {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Runtime : .NET {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Machine : {Environment.MachineName}");
        sb.AppendLine($"User    : {Environment.UserName}");
        sb.AppendLine(new string('─', 64));
        AppendException(sb, ex, depth: 0);

        File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
        return logPath;
    }

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        string pad = new(' ', depth * 2);

        sb.AppendLine($"{pad}Exception : {ex.GetType().FullName}");
        sb.AppendLine($"{pad}Message   : {ex.Message}");
        sb.AppendLine($"{pad}StackTrace:");

        foreach (string frame in (ex.StackTrace ?? "(no stack trace)").Split('\n'))
        {
            sb.AppendLine($"{pad}  {frame.TrimEnd()}");
        }

        if (ex is AggregateException agg)
        {
            foreach (Exception inner in agg.InnerExceptions)
            {
                sb.AppendLine();
                sb.AppendLine($"{pad}─ InnerException:");
                AppendException(sb, inner, depth + 1);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}─ InnerException:");
            AppendException(sb, ex.InnerException, depth + 1);
        }
    }

    private static void LaunchBugTracker(string logPath)
    {
        string? baseDir = Path.GetDirectoryName(Environment.ProcessPath);
        string trackerPath = baseDir != null
            ? Path.Combine(baseDir, BugTrackerExe)
            : BugTrackerExe;

        if (!File.Exists(trackerPath))
        {
            trackerPath = BugTrackerExe;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName        = trackerPath,
            Arguments       = $"--crash-report \"{logPath}\" --app \"PDownloader\"",
            UseShellExecute = true,
        });
    }
}