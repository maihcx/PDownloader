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

namespace PDownloader.Dialogs.ViewModels;

public partial class AddLink : ObservableObject, IDataErrorInfo
{
    [ObservableProperty]
    private string _link = string.Empty;

    public AddLink()
    {
        LoadClipboard();
    }

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            switch (columnName)
            {
                case nameof(Link):
                    if (string.IsNullOrWhiteSpace(Link))
                    {
                        return "Link is required";
                    }

                    break;
            }

            return string.Empty;
        }
    }

    private void LoadClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            string text = Clipboard.GetText().Trim();

            if (IsValidUrl(text))
            {
                Link = text;
            }
        }
        catch { }
    }

    public bool IsValidUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();

        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
               && (uri.Scheme == Uri.UriSchemeHttp ||
                   uri.Scheme == Uri.UriSchemeHttps);
    }
}
