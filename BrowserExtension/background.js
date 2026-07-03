// ============================================================
// PDownloader Extension — Background Service Worker (entry point)
// Port: localhost:6287 (PDownloader.Core HttpBridgeService)
//
// File này CHỈ làm 2 việc: nạp các module (importScripts, service worker
// classic type nên importScripts dùng được — không cần "type":"module")
// và khởi tạo chúng. Toàn bộ logic thật nằm trong common/ và background/,
// mỗi file 1 trách nhiệm rõ ràng, gắn vào namespace chung `self.PD` (xem
// PD.Constants, PD.State, PD.Utils, PD.Storage, PD.Api, PD.Badge,
// PD.Notify, PD.ContextMenu, PD.HlsCapture, PD.ContentDisposition,
// PD.DownloadIntercept, PD.MessageRouter).
//
// Thứ tự nạp quan trọng: file sau có thể dùng PD.<x> do file trước định
// nghĩa (vd storage.js cần Constants, downloadIntercept.js cần Utils +
// ContentDisposition + Storage + Api + Badge + Notify).
// ============================================================
importScripts(
  'common/i18n.js',

  'background/constants.js',
  'background/state.js',
  'background/utils.js',
  'background/storage.js',

  'background/badge.js',
  'background/notifications.js',
  'background/api.js',

  'background/contextMenu.js',
  'background/hlsCapture.js',
  'background/contentDisposition.js',
  'background/downloadIntercept.js',
  'background/messageRouter.js'
);

// ============================================================
// INIT
// ============================================================
chrome.runtime.onInstalled.addListener(() => {
  self.PD.Storage.seedDefaultsOnInstall();
  self.PD.ContextMenu.createMenus();
});

chrome.runtime.onStartup.addListener(() => self.PD.ContextMenu.createMenus());

// Các listener sau đều PHẢI đăng ký đồng bộ ở top-level (không lồng trong
// onInstalled/onStartup) để MV3 luôn đánh thức lại được service worker khi
// có sự kiện tương ứng, kể cả sau khi nó đã bị idle-unload.
self.PD.ContextMenu.init();
self.PD.HlsCapture.init();
self.PD.ContentDisposition.init();
self.PD.DownloadIntercept.init();
self.PD.MessageRouter.init();
