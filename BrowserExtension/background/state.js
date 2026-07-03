// ============================================================
// PD.State — state runtime của service worker (mất khi service worker bị
// Chrome unload; interceptCount vốn dĩ đã luôn là "đếm theo phiên", không
// persist, nên hành vi này không đổi so với bản gốc).
// ============================================================
(function (root) {
  const PD = root.PD || (root.PD = {});

  let interceptCount = 0;

  PD.State = {
    getInterceptCount: () => interceptCount,
    resetInterceptCount() { interceptCount = 0; },
    incrementInterceptCount() { interceptCount++; return interceptCount; },

    // url → { filename, timestamp } — cache Content-Disposition đọc được qua webRequest
    cdCache: new Map(),

    // tabId -> { url, referer, foundAt } — manifest HLS/DASH "nghe lén" được qua webRequest
    hlsManifestsByTab: new Map()
  };
})(self);
