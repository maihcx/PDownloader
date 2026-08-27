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

    public Dictionary<string, string>? CustomHeaders { get; set; }

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
                    case RunnerLaunchProtocol.TokenArgument: cfg.Token = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case RunnerLaunchProtocol.UrlArgument: cfg.InitialUrl = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case RunnerLaunchProtocol.SaveToArgument: cfg.SaveTo = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case RunnerLaunchProtocol.FileNameArgument: cfg.FileName = Helpers.Base64Decode(args[i + 1].Trim()); break;
                    case RunnerLaunchProtocol.ThreadsArgument: if (int.TryParse(Helpers.Base64Decode(args[i + 1].Trim()), out var t)) { cfg.Threads = t; } break;
                    case RunnerLaunchProtocol.DownloadRunnerArgument: cfg.IsRunner = Helpers.Base64Decode(args[i + 1].Trim()) == RunnerLaunchProtocol.RunnerModeValue; break;
                    case RunnerLaunchProtocol.HeadersArgument:
                        try
                        {
                            string json = Helpers.Base64Decode(args[i + 1].Trim());
                            cfg.CustomHeaders = System.Text.Json.JsonSerializer
                                .Deserialize<Dictionary<string, string>>(json);
                        }
                        catch { }

                        break;
                }
            }
        }

        return cfg;
    }
}
