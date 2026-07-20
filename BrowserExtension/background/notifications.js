(function (root) {
  const PD = root.PD || (root.PD = {});

  function createBasicNotification({ idPrefix, title, message, priority = 1, autoClearMs = 0 }) {
    return new Promise((resolve, reject) => {
      const id = `${idPrefix}-${Date.now()}`;

      chrome.notifications.create(id, {
        type: 'basic',
        iconUrl: 'icons/icon128.png',
        title,
        message,
        priority
      }, createdId => {
        const error = chrome.runtime.lastError;
        if (error) {
          reject(new Error(error.message));
          return;
        }

        if (autoClearMs > 0) {
          setTimeout(() => chrome.notifications.clear(createdId || id), autoClearMs);
        }

        resolve(createdId || id);
      });
    });
  }

  async function show(label) {
    const s = await chrome.storage.local.get(['showNotifications']);
    if (s.showNotifications === false) return;

    const fallback = PD.I18n.t('notifDefaultLabel');
    const display = label && label.length > 55 ? label.slice(0, 52) + '…' : (label || fallback);

    await createBasicNotification({
      idPrefix: 'pd-download',
      title: PD.I18n.t('notifTitle'),
      message: display,
      priority: 1,
      autoClearMs: 4000
    });
  }

  function showExtensionUpdated() {
    return createBasicNotification({
      idPrefix: 'pd-extension-updated',
      title: PD.I18n.t('extensionUpdatedNotificationTitle'),
      message: PD.I18n.t('extensionUpdatedNotificationMessage'),
      priority: 2
    });
  }

  PD.Notify = { show, showExtensionUpdated };
})(self);
