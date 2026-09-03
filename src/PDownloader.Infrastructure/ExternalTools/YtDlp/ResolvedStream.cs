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

namespace PDownloader.Infrastructure.ExternalTools.YtDlp;

public sealed class ResolvedStream
{
    public string FormatId { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Ext { get; set; } = "mp4";

    public bool HasVideo { get; set; }

    public bool HasAudio { get; set; }

    public long FilesizeApprox { get; set; }

    public bool IsFilesizeEstimated { get; set; }

    public Dictionary<string, string> HttpHeaders { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsDirectHttp
    {
        get
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp
                    && uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(Protocol)
                || Protocol.Equals("http", StringComparison.OrdinalIgnoreCase)
                || Protocol.Equals("https", StringComparison.OrdinalIgnoreCase);
        }
    }
}
