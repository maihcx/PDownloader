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

namespace PDownloader.TorrentShell;

public partial class TorrentShellConfig : ObservableObject
{
    [ObservableProperty]
    private string _token = string.Empty;

    [ObservableProperty]
    private string _torrentName = string.Empty;

    [ObservableProperty]
    private string _infoHash = string.Empty;

    [ObservableProperty]
    private string _saveTo = string.Empty;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private bool _hasStarted;

    public ObservableCollection<TorrentFileViewModel> Files { get; } = [];

    public static TorrentShellConfig Parse(string[] args)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(
                    args[index],
                    TorrentShellLaunchProtocol.TokenArgument,
                    StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                string token = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(args[index + 1].Trim()));
                return new TorrentShellConfig { Token = token };
            }
            catch (FormatException)
            {
                break;
            }
        }

        throw new InvalidDataException(
            LanguageBase.GetLangValue("torrent_shell_invalid_token_error"));
    }

    public void ApplySession(TorrentShellSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        TorrentName = string.IsNullOrWhiteSpace(session.Name)
            ? LanguageBase.GetLangValue("torrent_shell_default_name")
            : session.Name;
        InfoHash = session.InfoHash;
        SaveTo = session.SaveTo;
        TotalBytes = session.TotalBytes;
        Files.Clear();
        foreach (TorrentShellFileDto file in session.Files)
        {
            Files.Add(new TorrentFileViewModel(file));
        }
    }
}
