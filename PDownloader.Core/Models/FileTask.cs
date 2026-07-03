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

namespace PDownloader.Core.Models;

public class FileTask
{
    public string id { get; set; } = string.Empty;

    public string url { get; set; } = string.Empty;

    public string formatId { get; set; } = string.Empty;

    public string saveTo { get; set; } = string.Empty;

    public string fileName { get; set; } = string.Empty;

    public string title { get; set; } = string.Empty;

    public long filesize { get; set; }

    public string downloadRunner { get; set; } = string.Empty;

    public int threads { get; set; } = 0;

    public Dictionary<string, string>? headers { get; set; }
}
