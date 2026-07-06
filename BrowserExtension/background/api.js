(function (root) {
  const PD = root.PD || (root.PD = {});
  const C = PD.Constants;

  async function ping() {
    try {
      const res = await fetch(C.PING_URL, { signal: AbortSignal.timeout(2000) });
      return res.ok;
    } catch (_) { return false; }
  }

  async function sendDownload(url, filename, referer) {
    const cookies = await getCookieHeader(url);

    const payload = {
      url,
      fileName: filename || null,
      saveTo:   '',
      headers: {
        Cookie:       cookies,
        Referer:      referer || '',
        'User-Agent': navigator.userAgent
      }
    };

    try {
      const res = await fetch(C.DOWNLOAD_URL, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(payload)
      });
      return res.ok;
    } catch (_) {
      return false;
    }
  }

  async function getCookieHeader(url) {
    try {
      const all = await chrome.cookies.getAll({ url });
      return all.map(c => `${c.name}=${c.value}`).join('; ');
    } catch (_) {
      return '';
    }
  }

  async function getGoogleCookieHeader() {
    try {
      const all = await chrome.cookies.getAll({ domain: 'google.com' });
      return all.map(c => `${c.name}=${c.value}`).join('; ');
    } catch (_) {
      return '';
    }
  }

  async function postJson(url, body) {
    try {
      const r = await fetch(url, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(body)
      });
      return r.ok ? await r.json() : { success: false, error: `Server ${r.status}` };
    } catch (_) {
      return { success: false, error: PD.I18n.t('connErrorGeneric') };
    }
  }

  async function ytAnalyze(url) {
    const [cookies, googleCookies] = await Promise.all([
      getCookieHeader(url),
      getGoogleCookieHeader()
    ]);

    const headers = {};
    if (cookies) headers.Cookie = cookies;
    if (googleCookies) headers['X-Google-Cookie'] = googleCookies;

    return postJson(C.YT_ANALYZE_URL, {
      url,
      headers: Object.keys(headers).length ? headers : undefined
    });
  }

  async function ytDownload({ url, formatId, filename, title, filesize, referer }) {
    const [cookies, googleCookies] = await Promise.all([
      getCookieHeader(url),
      getGoogleCookieHeader()
    ]);

    const headers = {};
    if (cookies) headers.Cookie = cookies;
    if (googleCookies) headers['X-Google-Cookie'] = googleCookies;
    if (referer) headers.Referer = referer;

    return postJson(C.YT_DOWNLOAD_URL, {
      url,
      formatId,
      filename,
      title:    title || filename,
      filesize: filesize || 0,
      headers:  Object.keys(headers).length ? headers : undefined
    });
  }

  PD.Api = { ping, sendDownload, ytAnalyze, ytDownload };
})(self);