document.addEventListener('DOMContentLoaded', async () => {
  const statusCard    = document.getElementById('statusCard');
  const statusText    = document.getElementById('statusText');
  const interceptEl   = document.getElementById('interceptCount');
  const badgeEl       = document.getElementById('badgeCount');
  const autoChk       = document.getElementById('autoIntercept');
  const notifChk      = document.getElementById('showNotifications');
  const blList        = document.getElementById('blList');

  // ── Ping app ────────────────────────────────────────────────────────────────
  chrome.runtime.sendMessage({ action: 'ping_app' }, res => {
    const ok = res?.connected;
    statusCard.className = 'status-card ' + (ok ? 'ok' : 'err');
    statusText.textContent = ok ? '✓ Đang kết nối với PDownloader' : '✗ Chưa kết nối — hãy mở ứng dụng';
  });

  // ── Count ───────────────────────────────────────────────────────────────────
  chrome.runtime.sendMessage({ action: 'get_intercept_count' }, res => {
    const n = res?.count ?? 0;
    interceptEl.textContent = n;
    badgeEl.textContent = n + ' đã bắt';
  });
  chrome.runtime.sendMessage({ action: 'reset_badge' });

  // ── Load settings ───────────────────────────────────────────────────────────
  chrome.runtime.sendMessage({ action: 'get_settings' }, data => {
    autoChk.checked  = data.autoIntercept     !== false;
    notifChk.checked = data.showNotifications !== false;
    renderBlacklist(data.blacklistedDomains || []);
  });

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
      blList.innerHTML = '<div class="bl-empty">Không có trang bị chặn</div>';
      return;
    }
    domains.forEach(d => {
      const row = document.createElement('div');
      row.className = 'bl-item';
      row.innerHTML = `<span class="bl-domain">${d}</span><button class="bl-rm" title="Xóa">✕</button>`;
      row.querySelector('.bl-rm').addEventListener('click', () => {
        chrome.runtime.sendMessage({ action: 'remove_blacklist', domain: d }, () => {
          chrome.runtime.sendMessage({ action: 'get_settings' }, data => renderBlacklist(data.blacklistedDomains || []));
        });
      });
      blList.appendChild(row);
    });
  }
});
