namespace PDownloader.Helpers
{
    internal class StatusToTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return string.Empty;

            var status = values[0] is DownloadStatus ds
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
}
