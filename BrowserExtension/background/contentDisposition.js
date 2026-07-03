// ============================================================
// PD.ContentDisposition — cache header Content-Disposition đọc được qua
// webRequest, dùng để xác định tên file thật khi chrome.downloads.onCreated
// chưa kịp resolve filename (race condition phổ biến với MV3).
// ============================================================
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
        // Content-Disposition chỉ thực sự có ý nghĩa trên các loại request có
        // khả năng là một file tải xuống. Image/stylesheet/font/script/media-
        // streaming gần như không bao giờ có header này, nên loại chúng ra
        // giúp giảm đáng kể số lần callback phải chạy trên mỗi trang.
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

  // Tra cứu filename theo danh sách URL ứng viên (thường là [finalUrl, url gốc]
  // — xem ghi chú trong downloadIntercept.js về lý do cần thử cả 2).
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
