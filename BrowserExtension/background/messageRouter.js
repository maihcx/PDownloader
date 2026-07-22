(function (root) {
  const PD = root.PD || (root.PD = {});
  const {
    Api,
    Badge,
    Notify,
    State,
    Storage,
    HlsCapture,
    Utils,
    MediaCandidateRegistry,
    MediaCapture
  } = PD;

  function resolveTabId(msg, sender) {
    const explicit = Number(msg?.tabId);
    if (Number.isInteger(explicit) && explicit >= 0) return explicit;
    return sender.tab?.id ?? -1;
  }

  function sanitizeMediaName(value, fallback = 'media') {
    const base = String(value || fallback)
      .replace(/[\\/:*?"<>|]/g, '_')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/[. ]+$/g, '')
      .slice(0, 120);
    return base || fallback;
  }

  function isGenericManifestName(value) {
    const leaf = String(value || '').split(/[/\\]/).pop().toLowerCase();
    return /^(?:index|master|playlist|manifest|stream)(?:[-_.][^.]*)?\.(?:m3u8|mpd)$/i.test(leaf)
      || /\.(?:m3u8|mpd)$/i.test(leaf);
  }

  function candidateFilename(candidate, fallbackTitle = '') {
    const isManifest = candidate?.kind === 'hls'
      || candidate?.kind === 'dash'
      || candidate?.mediaType === 'manifest';

    if (isManifest) {
      const rawFilename = candidate?.filename?.split(/[/\\]/).pop() || '';
      const filenameStem = rawFilename && !isGenericManifestName(rawFilename)
        ? rawFilename.replace(/\.[^.]+$/, '')
        : '';
      const title = candidate?.title || fallbackTitle || filenameStem || 'video';
      const extension = candidate?.mediaType === 'audio' ? '.m4a' : '.mp4';
      return `${sanitizeMediaName(title, candidate?.mediaType === 'audio' ? 'audio' : 'video')}${extension}`;
    }

    if (candidate?.filename) return candidate.filename.split(/[/\\]/).pop();

    const fromUrl = Utils.getFilenameFromUrl(candidate?.url || '');
    if (fromUrl) return fromUrl;

    const base = sanitizeMediaName(candidate?.title || fallbackTitle || 'media');
    return candidate?.mediaType === 'audio' ? `${base}.mp3` : `${base}.mp4`;
  }

  async function markSuccessfulCapture(candidate, label) {
    State.incrementInterceptCount();
    Badge.update();
    await Notify.show(label || candidateFilename(candidate));
  }

  async function downloadCandidate(candidate, fallbackTitle = '') {
    if (!candidate?.url) return { success: false, error: 'Media candidate is unavailable.' };

    const filename = candidateFilename(candidate, fallbackTitle);
    const isHls = candidate.kind === 'hls';
    const isDash = candidate.kind === 'dash';
    const useYtDlp = isDash || candidate.kind === 'page' || candidate.route === 'ytdlp';

    // Route HLS through the normal download endpoint. The Core already has a
    // dedicated HLS handler with fragment progress, merge checkpoints and
    // recovery. DASH still goes through yt-dlp because there is no native DASH
    // handler yet.
    if (isHls) {
      const ok = await Api.sendDownload(
        candidate.url,
        filename,
        candidate.referer || candidate.pageUrl || '',
        candidate.requestHeaders || {}
      );

      if (ok) await markSuccessfulCapture(candidate, filename);
      return { success: ok, error: ok ? undefined : PD.I18n.t('connErrorGeneric') };
    }

    if (useYtDlp) {
      const result = await Api.ytDownload({
        url: candidate.url,
        formatId: candidate.mediaType === 'audio' ? 'bestaudio/best' : 'bestvideo+bestaudio/best',
        filename,
        title: candidate.title || filename,
        filesize: candidate.size || 0,
        referer: candidate.referer || candidate.pageUrl || '',
        extraHeaders: candidate.requestHeaders || undefined
      });

      if (result?.success) await markSuccessfulCapture(candidate, filename);
      return result;
    }

    const ok = await Api.sendDownload(
      candidate.url,
      filename,
      candidate.referer || candidate.pageUrl || '',
      candidate.requestHeaders || {}
    );

    if (ok) await markSuccessfulCapture(candidate, filename);
    return { success: ok, error: ok ? undefined : PD.I18n.t('connErrorGeneric') };
  }

  const handlers = {
    ping_app(_msg, _sender, sendResponse) {
      Api.ping().then(ok => sendResponse({ connected: ok }));
      return true;
    },

    download(msg, _sender, sendResponse) {
      Api.sendDownload(msg.url, msg.filename || null, msg.referer || '', msg.headers || {}).then(ok => {
        if (ok) {
          State.incrementInterceptCount();
          Badge.update();
          Notify.show(msg.filename || msg.url);
        }
        sendResponse({ success: ok });
      });
      return true;
    },

    download_magnet(msg) {
      Api.sendDownload(msg.url, null, '').then(() => {});
      return false;
    },

    get_intercept_count(_msg, _sender, sendResponse) {
      sendResponse({ count: State.getInterceptCount() });
      return false;
    },

    reset_badge(_msg, _sender, sendResponse) {
      State.resetInterceptCount();
      Badge.update();
      sendResponse({ success: true });
      return false;
    },

    get_settings(_msg, _sender, sendResponse) {
      Storage.getSettings().then(sendResponse);
      return true;
    },

    get_popup_init(_msg, _sender, sendResponse) {
      Promise.all([Api.ping(), Storage.getSettings()]).then(([connected, settings]) => {
        sendResponse({ connected, interceptCount: State.getInterceptCount(), settings });
      });
      return true;
    },

    save_settings(msg, _sender, sendResponse) {
      Storage.saveSettings(msg.settings).then(() => sendResponse({ success: true }));
      return true;
    },

    add_blacklist(msg, _sender, sendResponse) {
      Storage.addBlacklist(msg.domain).then(() => sendResponse({ success: true }));
      return true;
    },

    remove_blacklist(msg, _sender, sendResponse) {
      Storage.removeBlacklist(msg.domain).then(() => sendResponse({ success: true }));
      return true;
    },

    get_site_status(msg, _sender, sendResponse) {
      (async () => {
        const url = msg.url || '';
        const domain = Utils.getDomain(url);
        const settings = await Storage.getSettings();
        const autoIntercept = settings.autoIntercept !== false;
        const incompatible = domain ? Utils.isIncompatibleSite(url) : false;
        const blacklisted = domain ? await Utils.isBlacklisted(url, settings.blacklistedDomains || []) : false;
        sendResponse({ domain, autoIntercept, incompatible, blacklisted });
      })();
      return true;
    },

    get_hls_manifest(_msg, sender, sendResponse) {
      const found = HlsCapture.get(sender.tab?.id);
      sendResponse(found ? { url: found.url, referer: found.referer } : null);
      return true;
    },

    register_media_candidate(msg, sender, sendResponse) {
      const tabId = sender.tab?.id ?? -1;
      const candidate = MediaCapture.registerContentCandidate(tabId, msg.candidate || {});
      sendResponse({ success: !!candidate, candidate });
      return false;
    },

    update_media_playback_state(msg, sender, sendResponse) {
      const tabId = sender.tab?.id ?? -1;
      MediaCapture.updatePlaybackState(tabId, sender.frameId ?? 0, msg.state || {});
      sendResponse({ success: tabId >= 0 });
      return false;
    },

    media_page_changed(msg, sender, sendResponse) {
      const tabId = sender.tab?.id ?? -1;
      if (tabId >= 0) {
        // Only a top-frame SPA navigation invalidates the whole tab registry.
        // Iframe navigations must not wipe candidates discovered by the page.
        if ((sender.frameId ?? 0) === 0) {
          MediaCandidateRegistry.clear(tabId);
          State.hlsManifestsByTab.delete(tabId);
        }
        MediaCapture.updatePlaybackState(tabId, sender.frameId ?? 0, { pageUrl: msg.url || '' });
      }
      sendResponse({ success: tabId >= 0 });
      return false;
    },

    get_media_candidates(msg, sender, sendResponse) {
      const tabId = resolveTabId(msg, sender);
      const candidates = MediaCandidateRegistry.getAll(tabId, {
        mediaType: msg.mediaType || '',
        minScore: Number.isFinite(msg.minScore) ? msg.minScore : 35,
        includeSegments: false
      });
      sendResponse({ candidates, playback: MediaCapture.getPlaybackState(tabId) });
      return false;
    },

    get_best_media_candidate(msg, sender, sendResponse) {
      const tabId = resolveTabId(msg, sender);
      const candidate = MediaCandidateRegistry.getBest(tabId, {
        mediaType: msg.mediaType || '',
        minScore: Number.isFinite(msg.minScore) ? msg.minScore : 45,
        includeSegments: false
      });
      sendResponse(candidate ? { candidate } : null);
      return false;
    },

    download_media_candidate(msg, sender, sendResponse) {
      const tabId = resolveTabId(msg, sender);
      const found = MediaCandidateRegistry.getById(tabId, msg.candidateId)
        || (msg.candidate?.url ? msg.candidate : null);
      const candidate = found && msg.mediaType
        ? { ...found, mediaType: msg.mediaType }
        : found;

      (async () => {
        let fallbackTitle = sender.tab?.title || '';
        if (!fallbackTitle && tabId >= 0) {
          try {
            fallbackTitle = (await chrome.tabs.get(tabId))?.title || '';
          } catch (_) { }
        }

        return downloadCandidate(candidate, fallbackTitle);
      })().then(sendResponse);
      return true;
    },

    download_via_ytdlp(msg, _sender, sendResponse) {
      Api.ytDownload({
        url: msg.url,
        formatId: msg.audioOnly ? 'bestaudio/best' : 'bestvideo+bestaudio/best',
        filename: msg.filename,
        title: msg.title || msg.filename,
        filesize: 0,
        referer: msg.referer,
        extraHeaders: msg.headers || undefined
      }).then(result => {
        if (result.success) {
          State.incrementInterceptCount();
          Badge.update();
          Notify.show(msg.filename || msg.title);
        }
        sendResponse(result);
      });
      return true;
    },

    analyze_youtube(msg, _sender, sendResponse) {
      Api.ytAnalyze(msg.url).then(sendResponse);
      return true;
    },

    download_youtube(msg, _sender, sendResponse) {
      Api.ytDownload({
        url: msg.url, formatId: msg.formatId, filename: msg.filename,
        title: msg.title, filesize: msg.filesize || 0
      }).then(sendResponse);
      return true;
    }
  };

  function init() {
    chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
      const handler = handlers[msg?.action];
      if (!handler) return false;
      return handler(msg, sender, sendResponse);
    });
  }

  PD.MessageRouter = { init };
})(self);
