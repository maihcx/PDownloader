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

chrome.runtime.onInstalled.addListener(() => {
  self.PD.Storage.seedDefaultsOnInstall();
  self.PD.ContextMenu.createMenus();
});

chrome.runtime.onStartup.addListener(() => self.PD.ContextMenu.createMenus());

self.PD.ContextMenu.init();
self.PD.HlsCapture.init();
self.PD.ContentDisposition.init();
self.PD.DownloadIntercept.init();
self.PD.MessageRouter.init();
