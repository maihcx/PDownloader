// ============================================================
// PD.HlsCapture — "nghe lén" request mạng để tìm URL manifest .m3u8/.mpd
// gốc, dùng cho các site phát video qua blob: URL (MediaSource Extensions
// — video.src không phải URL mạng thật nên không tải trực tiếp được).
// ============================================================
(function (root) {
  const PD = root.PD || (root.PD = {});
  const MANIFEST_URL_PATTERN = /\.(m3u8|mpd)(\?|$)/i;

  function init() {
    chrome.webRequest.onBeforeSendHeaders.addListener(
      (details) => {
        if (details.tabId < 0) return; // request không thuộc tab nào (vd service worker riêng của site)
        if (!MANIFEST_URL_PATTERN.test(details.url)) return;

        const refererHeader = details.requestHeaders?.find(
          h => h.name.toLowerCase() === 'referer'
        );

        PD.State.hlsManifestsByTab.set(details.tabId, {
          url:     details.url,
          referer: refererHeader?.value || details.documentUrl || details.initiator || '',
          foundAt: Date.now()
        });
      },
      { urls: ['<all_urls>'] },
      ['requestHeaders', 'extraHeaders']
    );

    // Xóa manifest cũ khi tab load trang mới, tránh lấy nhầm manifest của
    // tập phim trước đó.
    chrome.webRequest.onBeforeRequest.addListener(
      (details) => {
        if (details.type === 'main_frame') {
          PD.State.hlsManifestsByTab.delete(details.tabId);
        }
      },
      { urls: ['<all_urls>'] }
    );

    chrome.tabs.onRemoved.addListener((tabId) => PD.State.hlsManifestsByTab.delete(tabId));
  }

  function get(tabId) {
    return tabId != null ? PD.State.hlsManifestsByTab.get(tabId) : null;
  }

  PD.HlsCapture = { init, get };
})(self);
