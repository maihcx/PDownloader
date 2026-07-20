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

namespace PDownloader.ViewModels.PagesBottom;

public partial class AboutViewModel : ObservableObject
{
    public AboutViewModel()
    {
        InitializeViewModel();
    }

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _copyRight = AppInfoHelper.CopyRight;

    private void InitializeViewModel()
    {
        Version v = UpdateService.GetCurrentVersion();
        AppVersion = $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
