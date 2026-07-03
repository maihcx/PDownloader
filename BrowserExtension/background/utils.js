(function (root) {
  const PD = root.PD || (root.PD = {});

  function getDomain(url) {
    try { return new URL(url).hostname; } catch (_) { return ''; }
  }

  function getFilenameFromUrl(url) {
    try {
      const p = new URL(url).pathname;
      const s = p.substring(p.lastIndexOf('/') + 1);
      return s.includes('.') ? decodeURIComponent(s) : '';
    } catch (_) { return ''; }
  }

  function extractExt(url, filename) {
    if (filename) {
      const clean = filename.split(/[?#]/)[0];
      const parts = clean.split('.');
      if (parts.length > 1) { const e = parts.at(-1).toLowerCase().trim(); if (e.length <= 10) return e; }
    }
    try {
      const p = decodeURIComponent(new URL(url).pathname);
      const s = p.substring(p.lastIndexOf('/') + 1);
      const parts = s.split('.');
      if (parts.length > 1) { const e = parts.at(-1).toLowerCase().trim(); if (e.length <= 10) return e; }
    } catch (_) {}
    return '';
  }

  function matchExt(pattern, ext) {
    const p = pattern.trim().toLowerCase();
    if (p.includes('*')) return new RegExp('^' + p.replace(/\*/g, '.*') + '$').test(ext);
    return p === ext;
  }

  function matchMime(mime) {
    const m = mime.toLowerCase();
    return ['application/octet-stream','application/zip','application/x-rar',
            'application/x-7z','application/pdf','application/x-bittorrent',
            'video/','audio/'].some(t => m.startsWith(t));
  }

  function parseContentDisposition(val) {
    let m = val.match(/filename\*\s*=\s*(?:UTF-8''|utf-8'')([^;\s]+)/i);
    if (m) return decodeURIComponent(m[1]);
    m = val.match(/filename\s*=\s*"([^"]+)"/i);
    if (m) return m[1];
    m = val.match(/filename\s*=\s*([^;\s]+)/i);
    if (m) return m[1].replace(/^['"]|['"]$/g, '');
    return '';
  }

  async function isBlacklisted(url, list) {
    try {
      const d = getDomain(url);
      return list.some(b => d === b || d.endsWith('.' + b));
    } catch (_) { return false; }
  }

  PD.Utils = {
    getDomain, getFilenameFromUrl, extractExt, matchExt, matchMime,
    parseContentDisposition, isBlacklisted
  };
})(self);
