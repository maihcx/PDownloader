(function (root) {
  const PD = root.PD || (root.PD = {});
  const C = PD.Constants;

  const CLIENT_HEADER = 'X-PDownloader-Client';
  const CLIENT_VALUE = 'browser-extension';
  const TOKEN_HEADER = 'X-PDownloader-Token';
  const COOKIE_JAR_HEADER = 'X-PDownloader-Cookie-Jar';

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

  function cookieKey(cookie) {
    return [
      cookie?.storeId || '',
      cookie?.domain || '',
      cookie?.path || '/',
      cookie?.name || ''
    ].join('|');
  }

  function toPortableCookie(cookie) {
    if (!cookie?.name) return null;

    return {
      name: cookie.name,
      value: cookie.value || '',
      domain: cookie.domain || '',
      path: cookie.path || '/',
      secure: !!cookie.secure,
      httpOnly: !!cookie.httpOnly,
      hostOnly: !!cookie.hostOnly,
      session: !!cookie.session,
      expirationDate: Number.isFinite(cookie.expirationDate)
        ? cookie.expirationDate
        : null,
      sameSite: cookie.sameSite || '',
      storeId: cookie.storeId || ''
    };
  }

  function getKnownCookieDomains(url) {
    let host = '';
    try { host = new URL(url).hostname.toLowerCase(); } catch (_) { return []; }

    const domains = [
      'tiktok.com',
      'instagram.com',
      'facebook.com',
      'x.com',
      'twitter.com'
    ];

    return domains.filter(domain => host === domain || host.endsWith(`.${domain}`));
  }

  async function getCookieContext(primaryUrl, relatedUrl = '') {
    const urls = [...new Set([primaryUrl, relatedUrl]
      .filter(url => /^https?:/i.test(String(url || ''))))];
    const jar = new Map();
    let primaryCookies = [];

    for (let index = 0; index < urls.length; index++) {
      try {
        const cookies = await PDWebExt.cookies.getAll({ url: urls[index] });
        if (index === 0) primaryCookies = cookies;

        for (const cookie of cookies) {
          const portable = toPortableCookie(cookie);
          if (portable) jar.set(cookieKey(portable), portable);
        }
      } catch (_) { }
    }

    // Site extractors such as TikTok can touch several sibling hosts while
    // resolving a permalink. Preserve every cookie from the site's parent
    // domain in the Netscape jar; each cookie keeps its original path/domain,
    // so yt-dlp will still only send it where it is applicable.
    const cookieDomains = [...new Set(urls.flatMap(getKnownCookieDomains))];
    for (const domain of cookieDomains) {
      try {
        const cookies = await PDWebExt.cookies.getAll({ domain });
        for (const cookie of cookies) {
          const portable = toPortableCookie(cookie);
          if (portable) jar.set(cookieKey(portable), portable);
        }
      } catch (_) { }
    }

    return {
      header: primaryCookies.map(cookie => `${cookie.name}=${cookie.value}`).join('; '),
      cookies: [...jar.values()]
    };
  }

  function applyBrowserHeaders(headers) {
    headers['User-Agent'] = navigator.userAgent;

    if (!headers['Accept-Language'] && !headers['accept-language']) {
      const languages = Array.isArray(navigator.languages) && navigator.languages.length
        ? navigator.languages
        : [navigator.language].filter(Boolean);
      if (languages.length) headers['Accept-Language'] = languages.join(',');
    }
  }

  async function sendDownload(url, filename, referer, extraHeaders = {}) {
    const cookieContext = await getCookieContext(url, referer);

    const headers = {
      ...extraHeaders,
      Cookie: cookieContext.header,
      Referer: referer || extraHeaders.Referer || extraHeaders.referer || ''
    };
    applyBrowserHeaders(headers);
    if (cookieContext.cookies.length) headers[COOKIE_JAR_HEADER] = JSON.stringify(cookieContext.cookies);

    // Do not send empty optional headers.
    for (const key of Object.keys(headers)) {
      if (headers[key] == null || headers[key] === '') delete headers[key];
    }

    const payload = {
      url,
      fileName: filename || null,
      headers
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
    return (await getCookieContext(url)).header;
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
    const cookieContext = await getCookieContext(url);
    const headers = {};
    applyBrowserHeaders(headers);
    if (cookieContext.header) headers.Cookie = cookieContext.header;
    if (cookieContext.cookies.length) headers[COOKIE_JAR_HEADER] = JSON.stringify(cookieContext.cookies);

    return postJson(C.YT_ANALYZE_URL, {
      url,
      headers
    });
  }

  async function ytDownload({ url, formatId, filename, title, filesize, referer, extraHeaders }) {
    const cookieContext = await getCookieContext(url, referer);

    const headers = { ...(extraHeaders || {}) };
    if (cookieContext.header) headers.Cookie = cookieContext.header;
    if (referer) headers.Referer = referer;
    applyBrowserHeaders(headers);
    if (cookieContext.cookies.length) headers[COOKIE_JAR_HEADER] = JSON.stringify(cookieContext.cookies);

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
