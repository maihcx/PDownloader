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

namespace PDownloader.Core.Download.ExternalTools.YtDlp;

internal static class YtDlpFormatSelector
{
    private const string DefaultVideoSelector = "bestvideo+bestaudio/best";

    private const string VimeoProgressiveVideoSelector =
        "best[format_id^=http-]/" +
        "best[protocol=https][ext=mp4]/" +
        "best[protocol=http][ext=mp4]";

    public static string Normalize(string pageUrl, string requestedFormatId)
    {
        if (!IsVimeoUrl(pageUrl)
            || !string.Equals(
                requestedFormatId.Trim(),
                DefaultVideoSelector,
                StringComparison.OrdinalIgnoreCase))
        {
            return requestedFormatId;
        }

        return VimeoProgressiveVideoSelector;
    }

    private static bool IsVimeoUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Host.Equals("vimeo.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".vimeo.com", StringComparison.OrdinalIgnoreCase);
    }
}
