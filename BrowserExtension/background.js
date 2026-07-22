importScripts(
  'common/i18n.js',

  'background/constants.js',
  'background/state.js',
  'background/utils.js',
  'background/storage.js',

  'background/badge.js',
  'background/notifications.js',
  'background/versionHistory.js',
  'background/api.js',

  'background/contextMenu.js',
  'background/mediaCandidateRegistry.js',
  'background/mediaCapture.js',
  'background/hlsCapture.js',
  'background/contentDisposition.js',
  'background/downloadIntercept.js',
  'background/messageRouter.js'
);

function runVersionTask(task) {
  task.catch(error => {
    console.error('[PDownloader] Version history check failed:', error);
  });
}

chrome.runtime.onInstalled.addListener(details => {
  void (async () => {
    await self.PD.Storage.seedDefaultsOnInstall();
    await self.PD.VersionHistory.handleInstalled(details);
    self.PD.ContextMenu.createMenus();
  })().catch(error => {
    console.error('[PDownloader] Extension install/update initialization failed:', error);
  });
});

chrome.runtime.onStartup.addListener(() => {
  self.PD.ContextMenu.createMenus();
  runVersionTask(self.PD.VersionHistory.checkCurrentVersion('browser-startup'));
});

self.PD.ContextMenu.init();
self.PD.MediaCapture.init();
self.PD.HlsCapture.init();
self.PD.ContentDisposition.init();
self.PD.DownloadIntercept.init();
self.PD.MessageRouter.init();

// Manifest V3 service workers can be recreated at any time. Checking here makes
// the update notification resilient even if the onInstalled event was missed.
runVersionTask(self.PD.VersionHistory.checkCurrentVersion('service-worker-start'));
