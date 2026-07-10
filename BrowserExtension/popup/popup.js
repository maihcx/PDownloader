// Trích domain từ URL ngay trong popup, không cần round-trip qua background.
function getDomainFromUrl(url) {
  try { return new URL(url).hostname; } catch (_) { return ''; }
}

document.addEventListener('DOMContentLoaded', async () => {
  document.documentElement.lang = chrome.i18n.getUILanguage();
  PD.I18n.applyToDom(document);

  const statusCard      = document.getElementById('statusCard');
  const statusText      = document.getElementById('statusText');
  const interceptEl     = document.getElementById('interceptCount');
  const badgeEl         = document.getElementById('badgeCount');
  const autoChk         = document.getElementById('autoIntercept');
  const notifChk        = document.getElementById('showNotifications');
  const siteRow         = document.getElementById('autoInterceptSiteRow');
  const siteChk         = document.getElementById('autoInterceptSite');
  const siteSub         = document.getElementById('autoInterceptSiteSub');
  const blList          = document.getElementById('blList');

  let currentTabUrl = '';
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    currentTabUrl = tab?.url || '';
  } catch (_) { /* ignore */ }

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
    refreshSiteToggle();
  });

  chrome.runtime.sendMessage({ action: 'reset_badge' });

  autoChk.addEventListener('change', () => {
    save();
    refreshSiteToggle();
  });
  notifChk.addEventListener('change', () => save());

  siteChk.addEventListener('change', () => {
    const domain = getDomainFromUrl(currentTabUrl);
    if (!domain) return;

    const action = siteChk.checked ? 'remove_blacklist' : 'add_blacklist';
    chrome.runtime.sendMessage({ action, domain }, () => {
      chrome.runtime.sendMessage({ action: 'get_settings' }, data => {
        renderBlacklist(data.blacklistedDomains || []);
      });
    });
  });

  function save() {
    chrome.runtime.sendMessage({
      action: 'save_settings',
      settings: {
        autoIntercept:     autoChk.checked,
        showNotifications: notifChk.checked
      }
    });
  }

  function refreshSiteToggle() {
    chrome.runtime.sendMessage({ action: 'get_site_status', url: currentTabUrl }, status => {
      const { domain, autoIntercept, incompatible, blacklisted } = status || {};

      if (!autoIntercept) {
        setSiteToggle({ enabled: false, checked: !blacklisted, subKey: 'popupToggleAutoInterceptSiteSubGlobalOff' });
        return;
      }
      if (!domain) {
        setSiteToggle({ enabled: false, checked: false, subKey: 'popupToggleAutoInterceptSiteSubInvalid' });
        return;
      }
      if (incompatible) {
        setSiteToggle({ enabled: false, checked: false, subKey: 'popupToggleAutoInterceptSiteSubIncompatible', sub: [domain] });
        return;
      }
      setSiteToggle({ enabled: true, checked: !blacklisted, subKey: 'popupToggleAutoInterceptSiteSubDomain', sub: [domain] });
    });
  }

  function setSiteToggle({ enabled, checked, subKey, sub }) {
    siteChk.disabled = !enabled;
    siteChk.checked = checked;
    siteRow.classList.toggle('is-disabled', !enabled);
    siteSub.textContent = PD.I18n.t(subKey, sub);
  }

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
          refreshSiteToggle();
        });
      });

      row.append(domainSpan, rmBtn);
      blList.appendChild(row);
    });
  }
});
