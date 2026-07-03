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

namespace PDownloader.Helpers;

internal class StatusToTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return string.Empty;
        }

        DownloadStatus status = values[0] is DownloadStatus ds
            ? ds
            : Enum.TryParse(values[0]?.ToString(), out DownloadStatus s)
                ? s
                : DownloadStatus.Error;

        string errorMessage = values[1]?.ToString() ?? string.Empty;

        return status switch
        {
            DownloadStatus.Queued =>
                LanguageBase.GetLangValue("download_status_queued_title"),

            DownloadStatus.Connecting =>
                LanguageBase.GetLangValue("download_status_connecting_title"),

            DownloadStatus.Downloading =>
                LanguageBase.GetLangValue("download_status_downloading_title"),

            DownloadStatus.Paused =>
                LanguageBase.GetLangValue("download_status_paused_title"),

            DownloadStatus.Merging =>
                LanguageBase.GetLangValue("download_status_merging_title"),

            DownloadStatus.Completed =>
                LanguageBase.GetLangValue("download_status_completed_title"),

            DownloadStatus.Cancelled =>
                LanguageBase.GetLangValue("download_status_cancelled_title"),

            DownloadStatus.Error =>
                LanguageBase.GetLangValue("download_status_error_title", errorMessage),

            _ =>
                LanguageBase.GetLangValue("download_status_error_title", "unknown...")
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
