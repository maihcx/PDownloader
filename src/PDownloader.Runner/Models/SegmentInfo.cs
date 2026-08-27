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

namespace PDownloader.Runner.Models;

/// <summary>One byte-range segment for a parallel/chunked download.</summary>
public class SegmentInfo
{
    public int Index { get; init; }
    public long RangeStart { get; init; }
    public long RangeEnd { get; init; }      // inclusive
    public long BytesWritten { get; set; }
    public string TempFilePath { get; init; } = string.Empty;
    public bool IsCompleted => BytesWritten >= (RangeEnd - RangeStart + 1);
    public long Length => RangeEnd - RangeStart + 1;
}
