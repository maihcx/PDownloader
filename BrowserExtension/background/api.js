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
    let cookies = '';
    try {
      const all = await chrome.cookies.getAll({ url });
      cookies = all.map(c => `${c.name}=${c.value}`).join('; ');
    } catch (_) {}

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

  function ytAnalyze(url) {
    return postJson(C.YT_ANALYZE_URL, { url });
  }

  function ytDownload({ url, formatId, filename, title, filesize, referer }) {
    return postJson(C.YT_DOWNLOAD_URL, {
      url,
      formatId,
      filename,
      title:    title || filename,
      filesize: filesize || 0,
      headers:  referer ? { Referer: referer } : undefined
    });
  }

  PD.Api = { ping, sendDownload, ytAnalyze, ytDownload };
})(self);
