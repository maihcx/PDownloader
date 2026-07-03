// ============================================================
// PD.DownloadIntercept — bắt download tự nhiên của trình duyệt
// (chrome.downloads.onCreated), quyết định có nên "cướp" sang PDownloader
// hay không, rồi hủy download gốc của trình duyệt nếu có.
// ============================================================
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

    // URL cuối cùng sau khi đi hết chuỗi redirect. Nhiều CDN (Cloudflare, S3,
    // ...) chỉ trả header Content-Disposition trên response CUỐI, không phải
    // trên URL gốc mà trang web trỏ tới (vd .../win/latest redirect sang một
    // URL file .msi thật). cdCache lại được ghi theo URL của đúng request đã
    // nhận header đó, nên phải tra cứu cả finalUrl lẫn url gốc.
    const finalUrl = item.finalUrl || url;

    let filename = item.filename || ContentDisposition.lookup([finalUrl, url]);

    // Tương tự, thử đoán phần mở rộng từ finalUrl trước (thường có tên file
    // thật) rồi mới fallback về url gốc.
    const ext  = Utils.extractExt(finalUrl, filename) || Utils.extractExt(url, filename);
    const exts = settings.extensions || Constants.DEFAULT_EXTENSIONS;
    const minBytes = ((settings.minInterceptSizeMb ?? 2)) * 1024 * 1024;

    const byExt  = ext && exts.some(p => Utils.matchExt(p, ext));
    const bySize = minBytes > 0 && item.fileSize > 0 && item.fileSize >= minBytes;
    const byMime = Utils.matchMime(item.mime || '');

    if (!byExt && !bySize && !byMime) return;

    // Cancel browser download
    chrome.downloads.cancel(item.id);
    chrome.downloads.erase({ id: item.id });

    let referer = '';
    try {
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      referer = tab?.url || '';
    } catch (_) {}

    // Nếu vẫn không có Content-Disposition, thử đoán tên từ path của finalUrl
    // (thường mang tên file thật) trước khi fallback về url gốc.
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
