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

namespace PDownloader.Contracts.Downloads;

/// <summary>
/// Cross-assembly request used when the download module asks Core to show/start a Runner.
/// This is an in-process contract between PDownloader.Downloads and PDownloader.Core;
/// the Runner process itself receives the values through RunnerLaunchProtocol arguments.
/// </summary>
public sealed class RunnerDownloadTask
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FormatId { get; set; } = string.Empty;
    public string SaveTo { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string RunnerMode { get; set; } = string.Empty;
    public int Threads { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}
