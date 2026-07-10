(function (root) {
  const PD = root.PD || (root.PD = {});
  const { Utils, ContentDisposition, Storage, Constants } = PD;

  function init() {
    chrome.downloads.onCreated.addListener(onCreated);
  }

  async function onCreated(item) {
    const settings = await Storage.getSettings(
      ['autoIntercept', 'extensions', 'minInterceptSizeMb', 'blacklistedDomains']
    );
    if (!settings.autoIntercept) return;

    const url = item.url || '';
    if (!url || url.startsWith('blob:') || url.startsWith('data:') || url.startsWith('chrome-extension:')) return;
    if (await Utils.isBlacklisted(url, settings.blacklistedDomains || [])) return;

    let activeTabUrl = '';
    try {
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      activeTabUrl = tab?.url || '';
    } catch (_) {}

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

    chrome.downloads.cancel(item.id);
    chrome.downloads.erase({ id: item.id });

    const referer = activeTabUrl;

    const displayName = filename
      ? filename.split(/[/\\]/).pop()
      : (Utils.getFilenameFromUrl(finalUrl) || Utils.getFilenameFromUrl(url) || null);

    const ok = await PD.Api.sendDownload(url, displayName, referer);
    if (ok) {
      PD.State.incrementInterceptCount();
      PD.Badge.update();
      await PD.Notify.show(displayName || Utils.getFilenameFromUrl(url));
    }
  }

  PD.DownloadIntercept = { init };
})(self);
