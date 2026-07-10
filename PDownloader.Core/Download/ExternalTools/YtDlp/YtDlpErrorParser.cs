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

internal static class YtDlpErrorParser
{
    public static string Parse(string? standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return "yt-dlp thất bại (không có thông tin lỗi).";
        }

        string[] lines = standardError.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? errorLine = lines.LastOrDefault(line =>
            line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));

        string message = errorLine ?? standardError.Trim();
        return message.Length <= 200 ? message : message[..200];
    }
}
