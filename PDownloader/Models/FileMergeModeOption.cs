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

public sealed class FileMergeModeOption : ObservableObject
{
    public FileMergeModeOption(
        string value,
        string titleKey,
        string descriptionKey)
    {
        Value = value;
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        LanguageBase.LanguageChanged += OnLanguageChanged;
    }

    public string Value { get; }

    public string TitleKey { get; }

    public string DescriptionKey { get; }

    public string Title => LanguageBase.GetLangValue(TitleKey);

    public string Description => LanguageBase.GetLangValue(DescriptionKey);

    private void OnLanguageChanged(string _)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}
