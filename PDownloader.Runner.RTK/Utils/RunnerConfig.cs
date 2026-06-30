namespace PDownloader.Runner.Utils;

public class RunnerConfig
{
    public string Token { get; set; } = string.Empty;
    public string InitialUrl  { get; set; } = string.Empty;
    public string SaveTo      { get; set; } = string.Empty;
    public string FileName    { get; set; } = string.Empty;
    public int    Threads     { get; set; } = 8;
    public string AccentColor { get; set; } = "#4FC3F7";

    public static RunnerConfig ParseArgs(string[] args)
    {
        var cfg = new RunnerConfig();
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--token":    cfg.Token       = args[i + 1]; break;
                case "--url":      cfg.InitialUrl  = args[i + 1]; break;
                case "--save-to":  cfg.SaveTo      = args[i + 1]; break;
                case "--filename": cfg.FileName    = args[i + 1]; break;
                case "--threads":  if (int.TryParse(args[i + 1], out var t)) cfg.Threads = t; break;
            }
        }
        return cfg;
    }
}
