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

namespace PDownloader.ControlsLookup;

[AttributeUsage(AttributeTargets.Class)]
public class PageMetaAttribute : Attribute, INotifyPropertyChanged
{
    public string DisplayName { get => Resources.Locales.String.ResourceManager.GetString(DisplayNameKey, TranslationSource.Instance.CurrentCulture) ?? string.Empty; }
    public string DisplayNameKey { get; }
    public string Description { get => Resources.Locales.String.ResourceManager.GetString(DescriptionKey, TranslationSource.Instance.CurrentCulture) ?? string.Empty; }
    public string DescriptionKey { get; }
    public SymbolRegular Icon { get; }
    public int SortIndex { get; }
    public bool IsShowPageTitle = true;

    public PageMetaAttribute(string displayName, string description, SymbolRegular icon, int sortIndex, bool isShowPageTitle)
    {
        DisplayNameKey = displayName;
        DescriptionKey = description;
        Icon = icon;
        SortIndex = sortIndex;
        IsShowPageTitle = isShowPageTitle;

        TranslationSource.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Description));
        };
    }

    public PageMetaAttribute(string displayName, string description, SymbolRegular icon, int sortIndex)
    {
        DisplayNameKey = displayName;
        DescriptionKey = description;
        Icon = icon;
        SortIndex = sortIndex;
    }

    public PageMetaAttribute(string displayName, string description, SymbolRegular icon)
    {
        DisplayNameKey = displayName;
        DescriptionKey = description;
        Icon = icon;
    }

    public PageMetaAttribute(string displayName, SymbolRegular icon)
    {
        DisplayNameKey = displayName;
        Icon = icon;
        DescriptionKey = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
