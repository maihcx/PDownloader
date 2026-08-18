(function (root) {
  const PD = root.PD || (root.PD = {});
  const C = PD.Constants;

  const CLIENT_HEADER = 'X-PDownloader-Client';
  const CLIENT_VALUE = 'browser-extension';
  const TOKEN_HEADER = 'X-PDownloader-Token';
  const COOKIE_JAR_HEADER = 'X-PDownloader-Cookie-Jar';
  const DOWNLOAD_TIMEOUT_MS = 15_000;
  const MEDIA_REQUEST_TIMEOUT_MS = 120_000;

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

  async function authorizedFetch(
    url,
    options = {},
    retryOnAuthFailure = true,
    timeoutMs = DOWNLOAD_TIMEOUT_MS
  ) {
    const token = await openSession();
    const headers = new Headers(options.headers || {});
    headers.set(CLIENT_HEADER, CLIENT_VALUE);
    headers.set(TOKEN_HEADER, token);

    const response = await fetch(url, {
      ...options,
      headers,
      signal: options.signal || AbortSignal.timeout(timeoutMs)
    });

    if (retryOnAuthFailure && (response.status === 401 || response.status === 403)) {
      sessionToken = null;
      await openSession(true);
      return authorizedFetch(url, options, false, timeoutMs);
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

  function normalizeTabId(value) {
    const tabId = Number(value);
    return Number.isInteger(tabId) && tabId >= 0 ? tabId : -1;
  }

  function normalizePartitionKey(partitionKey) {
    if (!partitionKey || typeof partitionKey !== 'object') return null;

    const topLevelSite = typeof partitionKey.topLevelSite === 'string'
      ? partitionKey.topLevelSite
      : '';
    const hasCrossSiteAncestor = typeof partitionKey.hasCrossSiteAncestor === 'boolean'
      ? partitionKey.hasCrossSiteAncestor
      : undefined;

    if (!topLevelSite && hasCrossSiteAncestor === undefined) return null;

    return {
      ...(topLevelSite ? { topLevelSite } : {}),
      ...(hasCrossSiteAncestor !== undefined ? { hasCrossSiteAncestor } : {})
    };
  }

  function cookieKey(cookie) {
    const partitionKey = normalizePartitionKey(cookie?.partitionKey);
    return JSON.stringify([
      cookie?.storeId || '',
      cookie?.firstPartyDomain || '',
      partitionKey?.topLevelSite || '',
      partitionKey?.hasCrossSiteAncestor === true ? '1'
        : partitionKey?.hasCrossSiteAncestor === false ? '0' : '',
      cookie?.domain || '',
      cookie?.path || '/',
      cookie?.name || ''
    ]);
  }

  function toPortableCookie(cookie) {
    if (!cookie?.name) return null;

    const partitionKey = normalizePartitionKey(cookie.partitionKey);

    const firstPartyDomain = typeof cookie.firstPartyDomain === 'string'
      ? cookie.firstPartyDomain
      : '';

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
      storeId: cookie.storeId || '',
      ...(firstPartyDomain ? { firstPartyDomain } : {}),
      ...(partitionKey ? { partitionKey } : {})
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

  function getHttpUrl(value) {
    try {
      const url = new URL(String(value || ''));
      return /^https?:$/i.test(url.protocol) ? url : null;
    } catch (_) {
      return null;
    }
  }

  function hostMatchesSite(host, siteValue) {
    const normalizedHost = String(host || '').toLowerCase().replace(/^\.+|\.+$/g, '');
    if (!normalizedHost || !siteValue) return false;

    let siteHost = '';
    try {
      siteHost = new URL(siteValue).hostname.toLowerCase();
    } catch (_) {
      siteHost = String(siteValue).toLowerCase().replace(/^\.+|\.+$/g, '');
    }

    if (!siteHost) return false;
    return normalizedHost === siteHost
      || normalizedHost.endsWith(`.${siteHost}`)
      || siteHost.endsWith(`.${normalizedHost}`);
  }

  async function resolveCookieSourceContext(sourceTabId, fallbackTopLevelUrl = '') {
    const tabId = normalizeTabId(sourceTabId);
    let tab = null;

    if (tabId >= 0) {
      try {
        tab = await PDWebExt.tabs.get(tabId);
      } catch (_) { }
    }

    const tabUrl = getHttpUrl(tab?.url);
    const fallbackUrl = getHttpUrl(fallbackTopLevelUrl);
    const topLevelUrl = tabUrl?.href || fallbackUrl?.href || '';
    const topLevelHost = tabUrl?.hostname || fallbackUrl?.hostname || '';

    let storeId = typeof tab?.cookieStoreId === 'string'
      ? tab.cookieStoreId
      : '';

    // Firefox exposes cookieStoreId directly on tabs (including Containers).
    // Chromium exposes the tab membership through CookieStore.tabIds instead.
    if (!storeId && tabId >= 0) {
      try {
        const stores = await PDWebExt.cookies.getAllCookieStores();
        const matchingStore = stores.find(store =>
          Array.isArray(store?.tabIds) && store.tabIds.includes(tabId));
        storeId = matchingStore?.id || '';
      } catch (_) { }
    }

    let partitionKey = null;

    // Chromium 132+ can resolve the actual schemeful partition key for a tab.
    // Use only topLevelSite when querying so both cross-site-ancestor variants
    // remain eligible; the returned cookie metadata is retained separately.
    if (tabId >= 0 && typeof PDWebExt.cookies?.getPartitionKey === 'function') {
      try {
        const resolved = normalizePartitionKey(
          await PDWebExt.cookies.getPartitionKey({ tabId, frameId: 0 }));
        if (resolved?.topLevelSite) {
          partitionKey = { topLevelSite: resolved.topLevelSite };
        }
      } catch (_) { }
    }

    if (!partitionKey && topLevelUrl) {
      try {
        partitionKey = { topLevelSite: new URL(topLevelUrl).origin };
      } catch (_) { }
    }

    return {
      tabId,
      storeId,
      topLevelUrl,
      topLevelHost: topLevelHost.toLowerCase(),
      partitionKey
    };
  }

  function cookieMatchesSourceContext(cookie, context) {
    if (context.storeId && cookie?.storeId && cookie.storeId !== context.storeId) {
      return false;
    }

    const firstPartyDomain = typeof cookie?.firstPartyDomain === 'string'
      ? cookie.firstPartyDomain
      : '';
    if (firstPartyDomain) {
      if (!context.topLevelHost || !hostMatchesSite(context.topLevelHost, firstPartyDomain)) {
        return false;
      }
    }

    const partitionKey = normalizePartitionKey(cookie?.partitionKey);
    if (partitionKey?.topLevelSite) {
      if (!context.topLevelHost || !hostMatchesSite(context.topLevelHost, partitionKey.topLevelSite)) {
        return false;
      }
    }

    return true;
  }

  function mergeCookies(...collections) {
    const merged = new Map();
    for (const collection of collections) {
      for (const cookie of collection || []) {
        if (cookie) merged.set(cookieKey(cookie), cookie);
      }
    }
    return [...merged.values()].sort((a, b) =>
      String(b?.path || '/').length - String(a?.path || '/').length);
  }

  async function queryCookies(details, context) {
    const scopedDetails = { ...details };
    if (context.storeId) scopedDetails.storeId = context.storeId;

    if (globalThis.PDWebExtPlatform?.isFirefox) {
      // Firefox can return every partition with partitionKey: {}. Combined with
      // firstPartyDomain: null this also works when First-Party Isolation is on.
      // Filter the result back to the source tab's top-level context so cookies
      // from other Containers/partitions are never flattened into the yt-dlp jar.
      try {
        const cookies = await PDWebExt.cookies.getAll({
          ...scopedDetails,
          firstPartyDomain: null,
          partitionKey: {}
        });
        return mergeCookies(cookies.filter(cookie => cookieMatchesSourceContext(cookie, context)));
      } catch (error) {
        console.warn('[PDownloader] Firefox contextual cookie query failed; falling back:', error);
      }

      try {
        const cookies = await PDWebExt.cookies.getAll({
          ...scopedDetails,
          firstPartyDomain: null
        });
        return mergeCookies(cookies.filter(cookie => cookieMatchesSourceContext(cookie, context)));
      } catch (_) { }
    }

    const unpartitioned = await PDWebExt.cookies.getAll(scopedDetails);
    let partitioned = [];

    if (context.partitionKey?.topLevelSite) {
      try {
        partitioned = await PDWebExt.cookies.getAll({
          ...scopedDetails,
          partitionKey: {
            topLevelSite: context.partitionKey.topLevelSite
          }
        });
      } catch (_) { }
    }

    return mergeCookies(
      unpartitioned.filter(cookie => cookieMatchesSourceContext(cookie, context)),
      partitioned.filter(cookie => cookieMatchesSourceContext(cookie, context))
    );
  }

  async function getCookieContext(primaryUrl, relatedUrl = '', sourceTabId = -1) {
    const urls = [...new Set([primaryUrl, relatedUrl]
      .filter(url => /^https?:/i.test(String(url || ''))))];
    const context = await resolveCookieSourceContext(
      sourceTabId,
      relatedUrl || primaryUrl);
    const jar = new Map();
    let primaryCookies = [];

    for (let index = 0; index < urls.length; index++) {
      try {
        const cookies = await queryCookies({ url: urls[index] }, context);
        if (index === 0) primaryCookies = cookies;

        for (const cookie of cookies) {
          const portable = toPortableCookie(cookie);
          if (portable) jar.set(cookieKey(portable), portable);
        }
      } catch (error) {
        console.warn('[PDownloader] Cookie query failed for URL:', urls[index], error);
      }
    }

    // Site extractors such as TikTok can touch several sibling hosts while
    // resolving a permalink. Preserve cookies from the site's parent domain,
    // but only from the same browser cookie store and top-level partition as
    // the tab that initiated the download.
    const cookieDomains = [...new Set(urls.flatMap(getKnownCookieDomains))];
    for (const domain of cookieDomains) {
      try {
        const cookies = await queryCookies({ domain }, context);
        for (const cookie of cookies) {
          const portable = toPortableCookie(cookie);
          if (portable) jar.set(cookieKey(portable), portable);
        }
      } catch (error) {
        console.warn('[PDownloader] Cookie query failed for domain:', domain, error);
      }
    }

    return {
      header: primaryCookies.map(cookie => `${cookie.name}=${cookie.value}`).join('; '),
      cookies: [...jar.values()]
    };
  }

  function normalizeVimeoPlayerUrl(rawUrl) {
    let url;
    try {
      url = new URL(String(rawUrl || ''));
    } catch (_) {
      return rawUrl;
    }

    if (url.hostname.toLowerCase() !== 'player.vimeo.com') {
      return rawUrl;
    }

    const match = url.pathname.match(/^\/video\/(\d+)(?:\/([^/?#]+))?\/?$/i);
    if (!match) return rawUrl;

    const videoId = match[1];
    const pathHash = match[2] || '';
    const queryHash = url.searchParams.get('h') || '';
    const unlistedHash = /^[A-Za-z0-9_-]+$/.test(pathHash)
      ? pathHash
      : (/^[A-Za-z0-9_-]+$/.test(queryHash) ? queryHash : '');

    return `https://vimeo.com/${videoId}`
      + (unlistedHash ? `/${unlistedHash}` : '');
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

  async function sendDownload(url, filename, referer, extraHeaders = {}, sourceTabId = -1) {
    const cookieContext = await getCookieContext(url, referer, sourceTabId);

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

  async function getCookieHeader(url, sourceTabId = -1) {
    return (await getCookieContext(url, '', sourceTabId)).header;
  }

  async function postJson(url, body, timeoutMs = MEDIA_REQUEST_TIMEOUT_MS) {
    try {
      const response = await authorizedFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      }, true, timeoutMs);

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
    } catch (error) {
      console.error('[PDownloader] Bridge JSON request failed:', url, error);
      return { success: false, error: PD.I18n.t('connErrorGeneric') };
    }
  }

  async function mediaAnalyze({
    url,
    referer,
    extraHeaders,
    sourceTabId = -1
  }) {
    const effectiveUrl = normalizeVimeoPlayerUrl(url);
    const cookieContext = await getCookieContext(effectiveUrl, referer, sourceTabId);
    const headers = { ...(extraHeaders || {}) };
    if (cookieContext.header) headers.Cookie = cookieContext.header;
    if (referer) headers.Referer = referer;
    applyBrowserHeaders(headers);
    if (cookieContext.cookies.length) headers[COOKIE_JAR_HEADER] = JSON.stringify(cookieContext.cookies);

    return postJson(C.MEDIA_ANALYZE_URL, {
      url: effectiveUrl,
      headers: Object.keys(headers).length ? headers : undefined
    });
  }

  async function mediaDownload({
    url,
    formatId,
    filename,
    title,
    filesize,
    referer,
    extraHeaders,
    sourceTabId = -1
  }) {
    const effectiveUrl = normalizeVimeoPlayerUrl(url);
    const cookieContext = await getCookieContext(effectiveUrl, referer, sourceTabId);

    const headers = { ...(extraHeaders || {}) };
    if (cookieContext.header) headers.Cookie = cookieContext.header;
    if (referer) headers.Referer = referer;
    applyBrowserHeaders(headers);
    if (cookieContext.cookies.length) headers[COOKIE_JAR_HEADER] = JSON.stringify(cookieContext.cookies);

    return postJson(C.MEDIA_DOWNLOAD_URL, {
      url: effectiveUrl,
      formatId,
      filename,
      title: title || filename,
      filesize: filesize || 0,
      headers: Object.keys(headers).length ? headers : undefined
    });
  }

  function ytAnalyze(url, sourceTabId = -1) {
    return mediaAnalyze({ url, sourceTabId });
  }

  function ytDownload(options) {
    return mediaDownload(options);
  }

  PD.Api = { ping, sendDownload, mediaAnalyze, mediaDownload, ytAnalyze, ytDownload };
})(self);
