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

namespace PDownloader.Models;

public class LanguageItem
{
    public string? Code { get; set; }
    public string? NativeName { get; set; }
    public string? EnglishName { get; set; }

    public override string ToString() => NativeName ?? string.Empty;

    public override bool Equals(object? obj)
    {
        return obj is LanguageItem other && this.Code == other.Code;
    }

    public override int GetHashCode()
    {
        return Code!.GetHashCode();
    }
}
