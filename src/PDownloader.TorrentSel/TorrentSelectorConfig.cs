// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.TorrentSel;

public sealed class TorrentSelectorConfig
{
    public string Token { get; private init; } = string.Empty;

    public static TorrentSelectorConfig Parse(string[] args)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(
                    args[index],
                    TorrentSelectionLaunchProtocol.TokenArgument,
                    StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                string token = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(args[index + 1].Trim()));
                return new TorrentSelectorConfig { Token = token };
            }
            catch (FormatException)
            {
                break;
            }
        }

        throw new InvalidDataException(
            LanguageBase.GetLangValue("torrent_selector_invalid_token_error"));
    }
}
