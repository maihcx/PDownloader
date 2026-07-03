(function (root) {
  const PD = root.PD || (root.PD = {});

  const APP_URL      = 'http://localhost:6287';

  PD.Constants = {
    APP_URL,
    PING_URL:     `${APP_URL}/ping`,
    DOWNLOAD_URL: `${APP_URL}/download`,
    YT_ANALYZE_URL:  `${APP_URL}/youtube/analyze`,
    YT_DOWNLOAD_URL: `${APP_URL}/youtube/download`,

    CACHE_TTL: 30000, // 30s — TTL cho content-disposition cache

    DEFAULT_EXTENSIONS: [
      // Archives
      'zip','rar','7z','tar','gz','bz2','xz','iso','cab','lzh','gzip','z',
      // Installers
      'exe','msi','msu','apk','dmg','pkg','deb','rpm','appimage',
      // Video
      'mp4','mkv','avi','mov','wmv','webm','flv','ts','m4v','3gp','mpeg','mpg','ogv','rm','rmvb',
      // Audio
      'mp3','wav','flac','ogg','m4a','aac','wma','opus',
      // Documents
      'pdf','epub','doc','docx','xls','xlsx','ppt','pptx',
      // Other
      'torrent','img','bin','dat','iso'
    ],

    DEFAULT_SETTINGS: {
      autoIntercept:      true,
      showNotifications:  true,
      blacklistedDomains: [],
      minInterceptSizeMb: 2
    },

    SETTINGS_KEYS: [
      'autoIntercept', 'extensions', 'showNotifications',
      'blacklistedDomains', 'minInterceptSizeMb'
    ]
  };
})(self);
