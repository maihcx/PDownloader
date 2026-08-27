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

public class NavigationCard : INotifyPropertyChanged
{
    private string _nameKey = string.Empty;
    private string _descriptionKey = string.Empty;

    public string NameKey
    {
        get => _nameKey;
        init
        {
            _nameKey = value;
        }
    }

    public string DescriptionKey
    {
        get => _descriptionKey;
        init
        {
            _descriptionKey = value;
        }
    }

    public string Name
    {
        get => string.IsNullOrEmpty(NameKey)
            ? string.Empty
            : TranslationSource.Instance[NameKey];
    }

    public string Description
    {
        get => string.IsNullOrEmpty(DescriptionKey)
            ? string.Empty
            : TranslationSource.Instance[DescriptionKey];
    }

    public SymbolRegular Icon { get; init; }

    public Type? PageType { get; init; } = null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public NavigationCard()
    {
        TranslationSource.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        };
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}