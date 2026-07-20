(function (root) {
  const PD = root.PD || (root.PD = {});
  const C = PD.Constants;

  const CLIENT_HEADER = 'X-PDownloader-Client';
  const CLIENT_VALUE = 'browser-extension';
  const TOKEN_HEADER = 'X-PDownloader-Token';

  let sessionToken = null;
  let sessionPromise = null;

  async function openSession(forceRefresh = false) {
    if (!forceRefresh && sessionToken) return sessionToken;
    if (!forceRefresh && sessionPromise) return sessionPromise;

    sessionPromise = (async () => {
      const res = await fetch(C.PING_URL, {
        method: 'GET',
        headers: {
          [CLIENT_HEADER]: CLIENT_VALUE
        },
        signal: AbortSignal.timeout(2000)
      });

      if (!res.ok) {
        throw new Error(`PDownloader bridge returned ${res.status}`);
      }

      const data = await res.json();
      if (!data?.ok || typeof data.token !== 'string' || !data.token) {
        throw new Error('PDownloader bridge did not return a session token');
      }

      sessionToken = data.token;
      return sessionToken;
    })();

    try {
      return await sessionPromise;
    } finally {
      sessionPromise = null;
    }
  }

  async function authorizedFetch(url, options = {}, retryOnAuthFailure = true) {
    const token = await openSession();
    const headers = new Headers(options.headers || {});
    headers.set(CLIENT_HEADER, CLIENT_VALUE);
    headers.set(TOKEN_HEADER, token);

    const response = await fetch(url, {
      ...options,
      headers
    });

    if (retryOnAuthFailure && (response.status === 401 || response.status === 403)) {
      sessionToken = null;
      await openSession(true);
      return authorizedFetch(url, options, false);
    }

    return response;
  }

  async function ping() {
    try {
      await openSession();
      return true;
    } catch (_) {
      sessionToken = null;
      return false;
    }
  }

  async function sendDownload(url, filename, referer) {
    const cookies = await getCookieHeader(url);

    const payload = {
      url,
      fileName: filename || null,
      headers: {
        Cookie: cookies,
        Referer: referer || '',
        'User-Agent': navigator.userAgent
      }
    };

    try {
      const res = await authorizedFetch(C.DOWNLOAD_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
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

  async function postJson(url, body) {
    try {
      const response = await authorizedFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      });

      let data = null;
      try {
        data = await response.json();
      } catch (_) { }

      if (response.ok) {
        return data ?? { success: true };
      }

      return {
        success: false,
        error: data?.error || `Server ${response.status}`
      };
    } catch (_) {
      return { success: false, error: PD.I18n.t('connErrorGeneric') };
    }
  }

  async function ytAnalyze(url) {
    const cookies = await getCookieHeader(url);
    return postJson(C.YT_ANALYZE_URL, {
      url,
      headers: cookies ? { Cookie: cookies } : undefined
    });
  }

  async function ytDownload({ url, formatId, filename, title, filesize, referer }) {
    const cookies = await getCookieHeader(url);

    const headers = {};
    if (cookies) headers.Cookie = cookies;
    if (referer) headers.Referer = referer;

    return postJson(C.YT_DOWNLOAD_URL, {
      url,
      formatId,
      filename,
      title: title || filename,
      filesize: filesize || 0,
      headers: Object.keys(headers).length ? headers : undefined
    });
  }

  PD.Api = { ping, sendDownload, ytAnalyze, ytDownload };
})(self);
