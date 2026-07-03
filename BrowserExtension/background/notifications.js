// ============================================================
// PD.Notify — thông báo hệ điều hành khi bắt được link.
// ============================================================
(function (root) {
  const PD = root.PD || (root.PD = {});

  async function show(label) {
    const s = await chrome.storage.local.get(['showNotifications']);
    if (s.showNotifications === false) return;

    const fallback = PD.I18n.t('notifDefaultLabel');
    const display = label && label.length > 55 ? label.slice(0, 52) + '…' : (label || fallback);
    const id = `pd-${Date.now()}`;

    chrome.notifications.create(id, {
      type:     'basic',
      iconUrl:  'icons/icon128.png',
      title:    PD.I18n.t('notifTitle'),
      message:  display,
      priority: 1
    });

    setTimeout(() => chrome.notifications.clear(id), 4000);
  }

  PD.Notify = { show };
})(self);
