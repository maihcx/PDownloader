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

    public static RunnerConfig ParseArgs(string[] args)
    {
        var cfg = new RunnerConfig();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == RunnerLaunchProtocol.TokenArgument)
            {
                cfg.Token = Helpers.Base64Decode(args[i + 1].Trim());
                break;
            }
        }

        cfg.IsArgsSetup = !string.IsNullOrWhiteSpace(cfg.Token);
        return cfg;
    }

    public void ApplySession(RunnerSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        InitialUrl = session.Url;
        SaveTo = session.SaveTo;
        FileName = session.FileName;
        Threads = session.Threads > 0 ? session.Threads : 8;
        IsRunner = session.IsRunner;
    }
}
