(function (root) {
  const PD = root.PD || (root.PD = {});
  const C = PD.Constants;

  function init() {
    chrome.webRequest.onHeadersReceived.addListener(
      (details) => {
        if (!details.responseHeaders) return;
        for (const h of details.responseHeaders) {
          if (h.name.toLowerCase() === 'content-disposition') {
            const fn = PD.Utils.parseContentDisposition(h.value || '');
            if (fn) {
              PD.State.cdCache.set(details.url, { filename: fn, timestamp: Date.now() });
              pruneCache();
            }
          }
        }
      },
      {
        urls: ['<all_urls>'],
        types: ['main_frame', 'sub_frame', 'xmlhttprequest', 'object', 'other']
      },
      ['responseHeaders']
    );
  }

  function pruneCache() {
    const now = Date.now();
    for (const [u, d] of PD.State.cdCache) {
      if (now - d.timestamp > C.CACHE_TTL) PD.State.cdCache.delete(u);
    }
  }

  function lookup(candidateUrls) {
    for (const candidate of new Set(candidateUrls)) {
      const cached = PD.State.cdCache.get(candidate);
      if (cached && Date.now() - cached.timestamp < C.CACHE_TTL) {
        PD.State.cdCache.delete(candidate);
        return cached.filename;
      }
    }
    return '';
  }

  PD.ContentDisposition = { init, lookup };
})(self);
