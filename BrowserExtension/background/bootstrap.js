function runVersionTask(task) {
  task.catch(error => {
    console.error('[PDownloader] Version history check failed:', error);
  });
}

PDWebExt.runtime.onInstalled.addListener(details => {
  void (async () => {
    await self.PD.Storage.seedDefaultsOnInstall();
    await self.PD.VersionHistory.handleInstalled(details);
    self.PD.ContextMenu.createMenus();
  })().catch(error => {
    console.error('[PDownloader] Extension install/update initialization failed:', error);
  });
});

PDWebExt.runtime.onStartup.addListener(() => {
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
