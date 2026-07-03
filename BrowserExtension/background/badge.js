// ============================================================
// PD.Badge — chấm số trên icon extension.
// ============================================================
(function (root) {
  const PD = root.PD || (root.PD = {});

  function update() {
    const n = PD.State.getInterceptCount();
    if (n > 0) {
      chrome.action.setBadgeText({ text: String(n) });
      chrome.action.setBadgeBackgroundColor({ color: '#4FC3F7' });
    } else {
      chrome.action.setBadgeText({ text: '' });
    }
  }

  PD.Badge = { update };
})(self);
