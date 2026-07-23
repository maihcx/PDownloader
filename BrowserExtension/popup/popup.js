// Trích domain từ URL ngay trong popup, không cần round-trip qua background.
function getDomainFromUrl(url) {
  try { return new URL(url).hostname; } catch (_) { return ''; }
}

document.addEventListener('DOMContentLoaded', async () => {
  document.documentElement.lang = PDWebExt.i18n.getUILanguage();
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
  const mediaList       = document.getElementById('mediaList');

  let currentTabUrl = '';
  let currentTabTitle = '';
  let currentTabId = -1;
  try {
    const [tab] = await PDWebExt.tabs.query({ active: true, currentWindow: true });
    currentTabUrl = tab?.url || '';
    currentTabTitle = tab?.title || '';
    currentTabId = Number.isInteger(tab?.id) ? tab.id : -1;
  } catch (_) { /* ignore */ }

  PDWebExt.runtime.sendMessage({ action: 'get_popup_init' }, res => {
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
    refreshDetectedMedia();
  });

  PDWebExt.runtime.sendMessage({ action: 'reset_badge' });

  autoChk.addEventListener('change', () => {
    save();
    refreshSiteToggle();
  });
  notifChk.addEventListener('change', () => save());

  siteChk.addEventListener('change', () => {
    const domain = getDomainFromUrl(currentTabUrl);
    if (!domain) return;

    const action = siteChk.checked ? 'remove_blacklist' : 'add_blacklist';
    PDWebExt.runtime.sendMessage({ action, domain }, () => {
      PDWebExt.runtime.sendMessage({ action: 'get_settings' }, data => {
        renderBlacklist(data.blacklistedDomains || []);
      });
    });
  });

  function save() {
    PDWebExt.runtime.sendMessage({
      action: 'save_settings',
      settings: {
        autoIntercept:     autoChk.checked,
        showNotifications: notifChk.checked
      }
    });
  }

  function refreshSiteToggle() {
    PDWebExt.runtime.sendMessage({ action: 'get_site_status', url: currentTabUrl }, status => {
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


  function formatBytes(bytes) {
    const value = Number(bytes) || 0;
    if (value <= 0) return '';
    if (value >= 1024 ** 3) return `${(value / (1024 ** 3)).toFixed(2)} GB`;
    if (value >= 1024 ** 2) return `${(value / (1024 ** 2)).toFixed(1)} MB`;
    if (value >= 1024) return `${(value / 1024).toFixed(0)} KB`;
    return `${value} B`;
  }

  function mediaTypeLabel(candidate) {
    if (candidate.kind === 'hls' || candidate.kind === 'dash' || candidate.mediaType === 'manifest') {
      return PD.I18n.t('popupMediaStream');
    }
    if (candidate.mediaType === 'audio') return PD.I18n.t('popupMediaAudio');
    if (candidate.mediaType === 'video') return PD.I18n.t('popupMediaVideo');
    return PD.I18n.t('popupMediaDirect');
  }

  function mediaIcon(candidate) {
    if (candidate.kind === 'hls' || candidate.kind === 'dash' || candidate.mediaType === 'manifest') return '◉';
    return candidate.mediaType === 'audio' ? '♪' : '▶';
  }

  function candidateName(candidate) {
    const isManifest = candidate.kind === 'hls'
      || candidate.kind === 'dash'
      || candidate.mediaType === 'manifest';

    if (isManifest) {
      return candidate.title || currentTabTitle || PD.I18n.t('popupMediaStream');
    }

    if (candidate.filename) return candidate.filename.split(/[/\\]/).pop();
    if (candidate.title) return candidate.title;
    try {
      const path = decodeURIComponent(new URL(candidate.url).pathname);
      return path.substring(path.lastIndexOf('/') + 1) || candidate.url;
    } catch (_) {
      return candidate.url || 'Media';
    }
  }

  function renderDetectedMedia(candidates, playback = null) {
    mediaList.replaceChildren();

    const visible = (candidates || [])
      .filter(candidate => !candidate.likelySegment)
      .slice(0, 8);

    if (!visible.length) {
      const empty = document.createElement('div');
      empty.className = 'media-empty';
      empty.textContent = PD.I18n.t('popupDetectedMediaEmpty');
      mediaList.appendChild(empty);
      return;
    }

    for (const candidate of visible) {
      const row = document.createElement('div');
      row.className = 'media-item';

      const icon = document.createElement('div');
      icon.className = 'media-icon';
      icon.textContent = mediaIcon(candidate);

      const main = document.createElement('div');
      main.className = 'media-main';

      const name = document.createElement('div');
      name.className = 'media-name';
      name.textContent = candidateName(candidate);
      name.title = candidate.url || '';

      const meta = document.createElement('div');
      meta.className = 'media-meta';
      meta.textContent = [
        mediaTypeLabel(candidate),
        candidate.extension ? candidate.extension.toUpperCase() : '',
        formatBytes(candidate.size)
      ].filter(Boolean).join(' · ');

      main.append(name, meta);

      const button = document.createElement('button');
      button.className = 'media-download';
      button.title = PD.I18n.t('popupMediaDownloadTitle');
      button.textContent = '↓';
      button.addEventListener('click', () => {
        button.disabled = true;
        PDWebExt.runtime.sendMessage({
          action: 'download_media_candidate',
          tabId: currentTabId,
          candidateId: candidate.id,
          mediaType: candidate.mediaType === 'audio'
            || ((candidate.kind === 'hls' || candidate.kind === 'dash') && playback?.playingAudio && !playback?.playingVideo)
              ? 'audio'
              : undefined
        }, response => {
          button.disabled = false;
          button.textContent = response?.success ? '✓' : '!';
          setTimeout(() => { button.textContent = '↓'; }, 1400);
        });
      });

      row.append(icon, main, button);
      mediaList.appendChild(row);
    }
  }

  function refreshDetectedMedia(allowRescan = true) {
    if (currentTabId < 0) {
      renderDetectedMedia([], null);
      return;
    }

    PDWebExt.runtime.sendMessage({
      action: 'get_media_candidates',
      tabId: currentTabId,
      minScore: 45
    }, response => {
      const candidates = response?.candidates || [];
      renderDetectedMedia(candidates, response?.playback || null);

      if (!candidates.length && allowRescan) {
        PDWebExt.tabs.sendMessage(currentTabId, { action: 'pd_rescan_media' }, () => {
          setTimeout(() => refreshDetectedMedia(false), 250);
        });
      }
    });
  }

  function renderBlacklist(domains) {
    blList.replaceChildren();
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
        PDWebExt.runtime.sendMessage({ action: 'remove_blacklist', domain: d }, () => {
          PDWebExt.runtime.sendMessage({ action: 'get_settings' }, data => renderBlacklist(data.blacklistedDomains || []));
          refreshSiteToggle();
        });
      });

      row.append(domainSpan, rmBtn);
      blList.appendChild(row);
    });
  }
});
