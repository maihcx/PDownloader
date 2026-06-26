namespace PDownloader.Tray.Resources
{
    public static class ThemeConfigs
    {
        public static WindowBackdropType WindowBackdropDefault = WindowBackdropType.Mica;

        public static WindowBackdropType IWindowBackdropType { get => new WindowBackdropType(); }

        public enum IThemeType
        {
            //
            // Summary:
            //     Auto application theme.
            Auto,
            //
            // Summary:
            //     Light application theme.
            Light,
            //
            // Summary:
            //     Dark application theme.
            Dark,
            //
            // Summary:
            //     High contract application theme.
            HighContrast,
            //
            // Summary:
            //     Unknown application theme.
            Unknown,
        }
    }
}
