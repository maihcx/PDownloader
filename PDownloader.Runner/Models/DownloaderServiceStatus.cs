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

public partial class DownloaderServiceStatus : ObservableObject
{
    public DownloaderServiceStatus()
    {
        LanguageBase.LanguageChanged += LanguageBase_LanguageChanged;
    }

    private void LanguageBase_LanguageChanged(string language)
    {
        OnErrorKeyChanged(ErrorKey);
        OnErrorKeyChanged(StatusKey);
    }

    [ObservableProperty]
    private string _errorKey = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _statusKey = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasError = false;

    [ObservableProperty]
    private bool _isSending = false;

    [ObservableProperty]
    private bool _isPaused = false;

    [ObservableProperty]
    private RunnerState _state = RunnerState.Form;

    partial void OnErrorKeyChanged(string value)
    {
        ErrorText = LanguageBase.GetLangValue(value);
    }

    partial void OnStatusKeyChanged(string value)
    {
        StatusText = LanguageBase.GetLangValue(value);
    }
}
