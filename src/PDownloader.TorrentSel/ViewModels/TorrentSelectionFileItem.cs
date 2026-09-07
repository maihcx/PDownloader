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

namespace PDownloader.TorrentSel.ViewModels;

public sealed class TorrentSelectionFileItem : ObservableObject
{
    private bool _isSelected = true;

    public TorrentSelectionFileItem(TorrentSelectionFileDto source)
    {
        Source = source;
    }

    public TorrentSelectionFileDto Source { get; }
    public int Index => Source.Index;
    public string FileName => Source.FileName;
    public string RelativePath => Source.RelativePath;
    public string SizeText => FormatBytes(Source.Length);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}
