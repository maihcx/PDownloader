(function (root) {
  const PD = root.PD || (root.PD = {});
  if (PD.MediaTitle) return;

  const GENERIC_TITLE = /^(?:video|audio|media|download|unknown|index|master|playlist|manifest|stream)(?:[\s_.-]*(?:audio|video|\d+p))?$/i;
  const MEDIA_EXTENSION = /\.(?:m3u8|mpd|mp4|m4a|mp3|webm|mkv|mov|avi|flv|wmv|mpeg|mpg|aac|ogg|opus)$/i;

  function clean(value, maxLength = 160) {
    return String(value || '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, maxLength);
  }

  function isGeneric(value) {
    const title = clean(value);
    if (!title) return true;
    if (/^https?:\/\//i.test(title)) return true;
    if (/^(?:www\.)?[a-z0-9.-]+\.[a-z]{2,}(?:[/:?#]|$)/i.test(title)) return true;

    const stem = title
      .replace(MEDIA_EXTENSION, '')
      .replace(/[. ]+$/g, '')
      .trim();
    return GENERIC_TITLE.test(stem);
  }

  function fromUrl(rawUrl) {
    try {
      const url = new URL(String(rawUrl || ''));
      let leaf = decodeURIComponent(url.pathname.split('/').filter(Boolean).at(-1) || '');
      leaf = leaf
        .replace(/\.[a-z0-9]{2,8}$/i, '')
        .replace(/[-_]+/g, ' ')
        .replace(/\s+\d{4,}$/g, '')
        .trim();
      return isGeneric(leaf) ? '' : clean(leaf);
    } catch (_) {
      return '';
    }
  }

  function pick(values, fallback = 'video') {
    for (const value of values || []) {
      const title = clean(value);
      if (title && !isGeneric(title)) return title;
    }
    return clean(fallback) || 'video';
  }

  function resolve(options = {}) {
    const pageUrlTitle = fromUrl(options.pageUrl);
    const mediaUrlTitle = fromUrl(options.mediaUrl);
    const values = options.isManifest
      ? [
          options.pageTitle,
          options.contextTitle,
          pageUrlTitle,
          options.analyzedTitle,
          mediaUrlTitle
        ]
      : [
          options.analyzedTitle,
          options.contextTitle,
          options.pageTitle,
          pageUrlTitle,
          mediaUrlTitle
        ];

    return pick(values, options.fallback || 'video');
  }

  function sanitize(value, fallback = 'video', maxLength = 120) {
    const name = clean(value || fallback, maxLength)
      .replace(/[\\/:*?"<>|]/g, '_')
      .replace(/[. ]+$/g, '')
      .trim();
    return name || fallback;
  }

  PD.MediaTitle = { clean, isGeneric, fromUrl, pick, resolve, sanitize };
})(self);
