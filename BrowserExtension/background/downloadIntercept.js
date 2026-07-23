(function (root) {
  const PD = root.PD || (root.PD = {});
  const { Utils, ContentDisposition, Storage, Constants } = PD;

  const INTERCEPT_SINCE_KEY = 'downloadInterceptSince';

  let interceptSincePromise = null;
  const processingDownloadIds = new Set();

  function init() {
    void getInterceptSince().catch(error => {
      console.error('[PDownloader] Failed to initialize download interception boundary:', error);
    });

    if (!PDWebExt.downloads.onCreated.hasListener(onCreated)) {
      PDWebExt.downloads.onCreated.addListener(onCreated);
    }
  }

  function getInterceptSince() {
    if (interceptSincePromise) return interceptSincePromise;

    const firstActivationAt = Date.now();

    interceptSincePromise = (async () => {
      const stored = await PDWebExt.storage.local.get([INTERCEPT_SINCE_KEY]);
      const existing = Number(stored[INTERCEPT_SINCE_KEY]);

      if (Number.isFinite(existing) && existing > 0) {
        return existing;
      }

      await PDWebExt.storage.local.set({
        [INTERCEPT_SINCE_KEY]: firstActivationAt
      });

      return firstActivationAt;
    })();

    return interceptSincePromise;
  }


  function normalizeDocumentUrl(value) {
    try {
      const url = new URL(String(value || ''));
      url.hash = '';
      return url.href;
    } catch (_) {
      return String(value || '').split('#')[0];
    }
  }

  async function resolveDownloadSourceTab(item) {
    let activeTab = null;
    try {
      [activeTab] = await PDWebExt.tabs.query({ active: true, currentWindow: true });
    } catch (_) { }

    const referrer = normalizeDocumentUrl(item?.referrer || '');
    if (!referrer) return activeTab;

    if (normalizeDocumentUrl(activeTab?.url || '') === referrer) {
      return activeTab;
    }

    try {
      const tabs = await PDWebExt.tabs.query({});
      const matches = tabs.filter(tab => normalizeDocumentUrl(tab?.url || '') === referrer);
      if (matches.length) {
        matches.sort((a, b) => Number(b?.lastAccessed || 0) - Number(a?.lastAccessed || 0));
        return matches[0];
      }
    } catch (_) { }

    return activeTab;
  }

  function isNewDownloadItem(item, interceptSince) {
    if (!item || typeof item.id !== 'number') return false;

    if (item.state !== 'in_progress') return false;

    const startedAt = Date.parse(item.startTime || '');

    if (!Number.isFinite(startedAt)) return false;

    return startedAt >= interceptSince;
  }

  async function onCreated(item) {
    if (processingDownloadIds.has(item?.id)) return;
    processingDownloadIds.add(item.id);

    try {
      const interceptSince = await getInterceptSince();
      if (!isNewDownloadItem(item, interceptSince)) return;

      const settings = await Storage.getSettings(
        ['autoIntercept', 'extensions', 'minInterceptSizeMb', 'blacklistedDomains']
      );
      if (!settings.autoIntercept) return;

      const url = item.url || '';
      if (!url || url.startsWith('blob:') || url.startsWith('data:') || url.startsWith('chrome-extension:')) return;
      if (await Utils.isBlacklisted(url, settings.blacklistedDomains || [])) return;

      const activeTab = await resolveDownloadSourceTab(item);
      const activeTabUrl = activeTab?.url || '';

      if (Utils.isIncompatibleSite(url) ||
          Utils.isIncompatibleSite(item.referrer || '') ||
          Utils.isIncompatibleSite(activeTabUrl)) return;

      const finalUrl = item.finalUrl || url;

      let filename = item.filename || ContentDisposition.lookup([finalUrl, url]);

      const ext  = Utils.extractExt(finalUrl, filename) || Utils.extractExt(url, filename);
      const exts = settings.extensions || Constants.DEFAULT_EXTENSIONS;
      const minBytes = ((settings.minInterceptSizeMb ?? 2)) * 1024 * 1024;

      const byExt  = ext && exts.some(p => Utils.matchExt(p, ext));
      const bySize = minBytes > 0 && item.fileSize > 0 && item.fileSize >= minBytes;
      const byMime = Utils.matchMime(item.mime || '');

      if (!byExt && !bySize && !byMime) return;

      await PDWebExt.downloads.cancel(item.id).catch(() => {});
      await PDWebExt.downloads.erase({ id: item.id }).catch(() => {});

      const referer = activeTabUrl;

      const displayName = filename
        ? filename.split(/[/\\]/).pop()
        : (Utils.getFilenameFromUrl(finalUrl) || Utils.getFilenameFromUrl(url) || null);

      const ok = await PD.Api.sendDownload(url, displayName, referer, {}, activeTab?.id ?? -1);
      if (ok) {
        PD.State.incrementInterceptCount();
        PD.Badge.update();
        await PD.Notify.show(displayName || Utils.getFilenameFromUrl(url));
      }
    } finally {
      processingDownloadIds.delete(item?.id);
    }
  }

  PD.DownloadIntercept = { init };
})(self);
