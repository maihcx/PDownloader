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

public enum DownloadSortMode
{
    NameAscending,
    NameDescending,
    TimeStartAscending,
    TimeStartDescending,
    TimeEndAscending,
    TimeEndDescending,
    SizeAscending,
    SizeDescending,
}

public partial class DownloadSortOption : ObservableObject, IDisposable
{
    public DownloadSortOption(DownloadSortMode mode, string displayKey)
    {
        Mode = mode;
        DisplayKey = displayKey;

        LanguageBase.LanguageChanged += LanguageBase_LanguageChanged;
    }

    private void LanguageBase_LanguageChanged(string language)
    {
        OnDisplayKeyChanged(DisplayKey);
    }

    [ObservableProperty]
    private DownloadSortMode _mode = DownloadSortMode.NameAscending;

    [ObservableProperty]
    private string _displayKey = string.Empty;

    partial void OnDisplayKeyChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            DisplayValue = string.Empty;
        }
        else
        {
            DisplayValue = LanguageBase.GetLangValue(value);
        }
    }

    [ObservableProperty]
    private string _displayValue = string.Empty;

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                LanguageBase.LanguageChanged -= LanguageBase_LanguageChanged;
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}