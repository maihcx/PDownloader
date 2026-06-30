using PDownloader.CFS;
using PDownloader.Runner.Views;

namespace PDownloader.Runner.Utils;

public static class AppRuntime
{
    public static RunnerConfig Config { get; set; } = new();

    /// <summary>CFS channel back to Core.</summary>
    public static ConfluxService? Cfs { get; set; }

    public static RunnerWindow? MainWindow { get; set; }

    public static bool IsDownloadStated { get; set; } = false;
}
