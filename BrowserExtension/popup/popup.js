document.addEventListener('DOMContentLoaded', async () => {
  document.documentElement.lang = chrome.i18n.getUILanguage();
  PD.I18n.applyToDom(document);

  const statusCard    = document.getElementById('statusCard');
  const statusText    = document.getElementById('statusText');
  const interceptEl   = document.getElementById('interceptCount');
  const badgeEl       = document.getElementById('badgeCount');
  const autoChk       = document.getElementById('autoIntercept');
  const notifChk      = document.getElementById('showNotifications');
  const blList        = document.getElementById('blList');

  // ── Khởi tạo popup: 1 round-trip duy nhất thay vì 3 lệnh tuần tự ─────────────
  // (ping_app + get_intercept_count + get_settings trước đây gọi riêng lẻ,
  // mỗi lần đều phải đánh thức service worker MV3 nếu nó đang ở trạng thái
  // ngủ — gộp lại giúp popup hiển thị nhanh hơn đáng kể, nhất là lần mở đầu.)
  chrome.runtime.sendMessage({ action: 'get_popup_init' }, res => {
    const ok = res?.connected;
    statusCard.className = 'status-card ' + (ok ? 'ok' : 'err');
    statusText.textContent = PD.I18n.t(ok ? 'popupStatusConnected' : 'popupStatusDisconnected');

    const n = res?.interceptCount ?? 0;
    interceptEl.textContent = n;
    badgeEl.textContent = PD.I18n.t('popupBadgeCaught', [String(n)]);

    const data = res?.settings || {};
    autoChk.checked  = data.autoIntercept     !== false;
    notifChk.checked = data.showNotifications !== false;
    renderBlacklist(data.blacklistedDomains || []);
  });

  // reset_badge vẫn là lệnh riêng vì nó có side-effect (xóa số đếm badge),
  // không nên gộp chung với lệnh chỉ đọc dữ liệu ở trên.
  chrome.runtime.sendMessage({ action: 'reset_badge' });

  // ── Toggle handlers ─────────────────────────────────────────────────────────
  autoChk.addEventListener('change', () => save());
  notifChk.addEventListener('change', () => save());

  function save() {
    chrome.runtime.sendMessage({
      action: 'save_settings',
      settings: {
        autoIntercept:     autoChk.checked,
        showNotifications: notifChk.checked
      }
    });
  }

  // ── Blacklist ────────────────────────────────────────────────────────────────
  function renderBlacklist(domains) {
    blList.innerHTML = '';
    if (!domains.length) {
      const empty = document.createElement('div');
      empty.className = 'bl-empty';
      empty.textContent = PD.I18n.t('popupBlacklistEmpty');
      blList.appendChild(empty);
      return;
    }
    domains.forEach(d => {
      const row = document.createElement('div');
      row.className = 'bl-item';

      const domainSpan = document.createElement('span');
      domainSpan.className = 'bl-domain';
      domainSpan.textContent = d;

      const rmBtn = document.createElement('button');
      rmBtn.className = 'bl-rm';
      rmBtn.title = PD.I18n.t('popupBlacklistRemoveTitle');
      rmBtn.textContent = '✕';
      rmBtn.addEventListener('click', () => {
        chrome.runtime.sendMessage({ action: 'remove_blacklist', domain: d }, () => {
          chrome.runtime.sendMessage({ action: 'get_settings' }, data => renderBlacklist(data.blacklistedDomains || []));
        });
      });

      row.append(domainSpan, rmBtn);
      blList.appendChild(row);
    });
  }
});
