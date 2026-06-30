namespace PDownloader.Runner.Utils;

public partial class RunnerConfig : ObservableObject
{
    [ObservableProperty]
    public string _token = string.Empty;

    [ObservableProperty]
    public string _initialUrl = string.Empty;

    [ObservableProperty]
    public string _saveTo = string.Empty;

    [ObservableProperty]
    public string _fileName = string.Empty;

    [ObservableProperty]
    public int _threads = 8;

    [ObservableProperty]
    public bool _isArgsSetup = false;

    [ObservableProperty]
    public bool _isRunner = false;

    public static RunnerConfig ParseArgs(string[] args)
    {
        var cfg = new RunnerConfig();
        if (args.Length == 0)
        {
            cfg.IsArgsSetup = false;
        }
        else
        {
            cfg.IsArgsSetup = true;
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--token": cfg.Token = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case "--url": cfg.InitialUrl = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case "--save-to": cfg.SaveTo = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case "--filename": cfg.FileName = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case "--threads": if (int.TryParse(args[i + 1], out var t)) cfg.Threads = t; break;
                    case "--download-runner": cfg.IsRunner = Helpers.Base64Decode(args[i + 1].Trim()) == "runner"; break;
                    //case "--download-status": if (int.TryParse(args[i + 1], out var status)) cfg.DownloadStatus = (DownloadStatus)status; break;
                }
            }
        }

        return cfg;
    }
}
