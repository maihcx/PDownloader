(function () {
  const AUDIO_EXTENSIONS = /\.(?:mp3|m4a|aac|flac|wav|ogg|oga|opus|weba|wma|alac)(?:[?#]|$)/i;
  const VIDEO_EXTENSIONS = /\.(?:mp4|webm|mkv|mov|m4v|avi|flv|wmv|3gp|mpeg|mpg|ogv)(?:[?#]|$)/i;
  const PDF_EXTENSIONS = /\.pdf(?:[?#]|$)/i;
  const MANIFEST_EXTENSIONS = /\.(?:m3u8|mpd)(?:[?#]|$)/i;
  const AUDIO_SITE_HINTS = [
    'soundcloud.com', 'bandcamp.com', 'mixcloud.com', 'audiomack.com', 'spotify.com'
  ];

  const IS_TOP_FRAME = window === window.top;
  let lastPageUrl = location.href;
  let bestAudioCandidate = null;
  let tabAudible = false;
  let audioButton = null;
  let audioHideTimer = null;
  let lastPlaybackState = '';
  let contextInvalidated = false;
  const observedMedia = new WeakSet();
  const reportedPerformanceUrls = new Set();

  function sendMessage(message) {
    if (contextInvalidated || !PDWebExt.runtime?.id) return Promise.resolve(null);

    return PDWebExt.runtime.sendMessage(message).catch(error => {
      const text = String(error?.message || error || '');
      if (/extension context invalidated|receiving end does not exist|message port closed/i.test(text)) {
        contextInvalidated = true;
      }
      return null;
    });
  }

  function sanitizeName(value, fallback = 'audio') {
    const name = String(value || fallback)
      .replace(/[\\/:*?"<>|]/g, '_')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, 100);
    return name || fallback;
  }

  function getMediaUrl(element) {
    const values = [
      element.currentSrc,
      element.src,
      element.getAttribute?.('src')
    ];

    for (const value of values) {
      if (value && /^https?:/i.test(value)) return value;
    }

    for (const source of element.querySelectorAll?.('source[src]') || []) {
      const value = source.src || source.getAttribute('src');
      if (value && /^https?:/i.test(value)) return value;
    }

    return '';
  }

  function getMediaMime(element) {
    if (element.currentSrc) {
      const source = [...(element.querySelectorAll?.('source') || [])]
        .find(item => item.src === element.currentSrc);
      if (source?.type) return source.type;
    }
    return element.getAttribute?.('type') || '';
  }

  function reportMediaElement(element, source = 'dom') {
    if (!(element instanceof HTMLMediaElement)) return;

    const mediaType = element instanceof HTMLAudioElement ? 'audio' : 'video';
    const url = getMediaUrl(element);
    const isPlaying = !element.paused && !element.ended && element.readyState > 0;

    if (url) {
      void sendMessage({
        action: 'register_media_candidate',
        candidate: {
          url,
          mediaType,
          kind: 'direct',
          mime: getMediaMime(element),
          title: document.title || mediaType,
          pageUrl: location.href,
          referer: location.href,
          source: isPlaying ? 'dom-playing' : source,
          score: isPlaying ? 165 : 105
        }
      });
    } else if (mediaType === 'audio' && isPlaying) {
      // Blob/WebAudio-style playback cannot be downloaded directly. Keep the
      // page URL as an yt-dlp fallback while the network registry looks for a
      // stronger direct audio or manifest candidate.
      void sendMessage({
        action: 'register_media_candidate',
        candidate: {
          url: location.href,
          mediaType: 'audio',
          kind: 'page',
          route: 'ytdlp',
          title: document.title || 'audio',
          filename: `${sanitizeName(document.title, 'audio')}.mp3`,
          pageUrl: location.href,
          referer: location.href,
          source: 'dom-playing',
          score: 75
        }
      });
    }
  }

  function isVisibleVideo(video) {
    if (!(video instanceof HTMLVideoElement) || !video.isConnected) return false;
    const rect = video.getBoundingClientRect();
    if (rect.width < 80 || rect.height < 50) return false;
    const style = getComputedStyle(video);
    return style.display !== 'none'
      && style.visibility !== 'hidden'
      && Number.parseFloat(style.opacity || '1') > 0.01;
  }

  function collectPlaybackState() {
    let playingAudio = false;
    let playingVideo = false;
    let visibleVideo = false;
    const activeAudioUrls = [];
    const mutedAudioUrls = [];
    const activeVideoUrls = [];

    for (const video of document.querySelectorAll('video')) {
      if (isVisibleVideo(video)) visibleVideo = true;

      if (!video.paused && !video.ended && video.readyState > 0) {
        playingVideo = true;
        const url = getMediaUrl(video);
        if (url) activeVideoUrls.push(url);
      }
    }

    for (const audio of document.querySelectorAll('audio')) {
      if (!audio.paused && !audio.ended && audio.readyState > 0) {
        playingAudio = true;
        const url = getMediaUrl(audio);
        if (url) {
          if (!audio.muted && audio.volume > 0) activeAudioUrls.push(url);
          else mutedAudioUrls.push(url);
        }
      }
    }

    return {
      playingAudio,
      playingVideo,
      visibleVideo,
      activeAudioUrls: [...new Set([...activeAudioUrls, ...mutedAudioUrls])],
      activeVideoUrls: [...new Set(activeVideoUrls)],
      pageUrl: location.href
    };
  }

  function publishPlaybackState(force = false) {
    const state = collectPlaybackState();
    const serialized = JSON.stringify(state);
    if (!force && serialized === lastPlaybackState) return state;

    lastPlaybackState = serialized;
    void sendMessage({ action: 'update_media_playback_state', state });
    refreshAudioButton(state);
    return state;
  }

  function observeMedia(element) {
    if (!(element instanceof HTMLMediaElement) || observedMedia.has(element)) return;
    observedMedia.add(element);

    const update = () => {
      reportMediaElement(element);
      publishPlaybackState(true);
    };

    for (const eventName of ['play', 'playing', 'loadedmetadata', 'durationchange', 'emptied', 'pause', 'ended']) {
      element.addEventListener(eventName, update, { passive: true });
    }

    reportMediaElement(element);
  }

  function scanMediaElements(root = document) {
    if (root instanceof HTMLMediaElement) observeMedia(root);
    for (const element of root.querySelectorAll?.('audio,video') || []) observeMedia(element);
    publishPlaybackState();
  }

  function classifyPerformanceUrl(url) {
    if (MANIFEST_EXTENSIONS.test(url)) return { mediaType: 'manifest', kind: /\.mpd(?:[?#]|$)/i.test(url) ? 'dash' : 'hls', score: 85 };
    if (AUDIO_EXTENSIONS.test(url)) return { mediaType: 'audio', kind: 'direct', score: 70 };
    if (VIDEO_EXTENSIONS.test(url)) return { mediaType: 'video', kind: 'direct', score: 70 };
    if (PDF_EXTENSIONS.test(url)) return { mediaType: 'pdf', kind: 'direct', score: 80 };
    return null;
  }

  function scanPerformanceEntries() {
    let entries = [];
    try { entries = performance.getEntriesByType('resource'); } catch (_) { }

    for (const entry of entries.slice(-400)) {
      const url = entry?.name || '';
      if (!url || reportedPerformanceUrls.has(url)) continue;

      const classification = classifyPerformanceUrl(url);
      if (!classification) continue;

      reportedPerformanceUrls.add(url);
      void sendMessage({
        action: 'register_media_candidate',
        candidate: {
          url,
          ...classification,
          size: Number(entry.transferSize) || Number(entry.encodedBodySize) || 0,
          title: document.title || 'media',
          pageUrl: location.href,
          referer: location.href,
          source: 'performance'
        }
      });
    }

    if (reportedPerformanceUrls.size > 1000) {
      const keep = [...reportedPerformanceUrls].slice(-500);
      reportedPerformanceUrls.clear();
      keep.forEach(url => reportedPerformanceUrls.add(url));
    }
  }

  function isAudioFocusedPage() {
    const hostname = location.hostname.toLowerCase();
    return AUDIO_SITE_HINTS.some(host =>
      hostname === host || hostname.endsWith(`.${host}`));
  }

  function isSpotifyPage() {
    return /(^|\.)spotify\.com$/i.test(location.hostname);
  }

  function isSpotifyPreviewUrl(value) {
    try {
      const url = new URL(String(value || ''));
      return url.protocol === 'https:'
        && /(^|\.)scdn\.co$/i.test(url.hostname)
        && /\/mp3-preview\//i.test(url.pathname);
    } catch (_) {
      return false;
    }
  }

  function getSpotifyTrackTitle() {
    const nowPlayingTitle = document.querySelector(
      '[data-testid="now-playing-widget"] [data-testid="context-item-info-title"]'
    )?.textContent?.trim();
    if (nowPlayingTitle) return nowPlayingTitle;

    const pageTitle = String(document.title || '').split(' • ')[0].trim();
    return pageTitle && !/^spotify(?:\s*[–-]\s*web player)?$/i.test(pageTitle)
      ? pageTitle
      : PD.I18n.t('contentSpotifyTrack');
  }

  function getDownloadableAudioCandidate() {
    if (!isSpotifyPage()) return bestAudioCandidate;
    return isSpotifyPreviewUrl(bestAudioCandidate?.url) ? bestAudioCandidate : null;
  }

  function isSpotifyProtectedPlayback(state = collectPlaybackState()) {
    if (!isSpotifyPage() || !tabAudible || state.playingVideo) return false;

    const hasPreview = isSpotifyPreviewUrl(bestAudioCandidate?.url)
      || (state.activeAudioUrls || []).some(isSpotifyPreviewUrl);
    return !hasPreview;
  }

  function canShowAudioButton(state) {
    const hostname = location.hostname.toLowerCase();
    const isYouTube = hostname === 'youtube.com'
      || hostname.endsWith('.youtube.com')
      || hostname === 'youtube-nocookie.com'
      || hostname.endsWith('.youtube-nocookie.com');
    if (!IS_TOP_FRAME || isYouTube) return false;
    if (state.playingVideo) return false;
    return state.playingAudio || (tabAudible && (!!bestAudioCandidate || isAudioFocusedPage()));
  }

  function ensureAudioButton() {
    if (audioButton) return audioButton;

    const style = document.createElement('style');
    style.textContent = `
      .pd-audio-grab-btn {
        position: fixed;
        right: 18px;
        bottom: 18px;
        z-index: 2147483647;
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px 12px;
        border-radius: 9px;
        border: 1px solid var(--pd-border, rgba(100,100,100,.3));
        background: var(--pd-bg, rgba(30,30,30,.92));
        color: var(--pd-text, #fff);
        box-shadow: 0 6px 24px rgba(0,0,0,.2);
        backdrop-filter: blur(14px);
        -webkit-backdrop-filter: blur(14px);
        font: 600 12px/1.2 'Segoe UI', system-ui, sans-serif;
        cursor: pointer;
        opacity: 0;
        visibility: hidden;
        transform: translateY(6px);
        transition: opacity .18s, transform .18s, border-color .15s;
      }
      .pd-audio-grab-btn:hover {
        border-color: var(--pd-accent, #4fc3f7);
      }
      .pd-audio-grab-btn.pd-visible {
        opacity: 1;
        visibility: visible;
        transform: translateY(0);
      }
      .pd-audio-grab-btn.pd-success {
        border-color: var(--pd-green, #4caf50);
      }
      .pd-audio-grab-btn.pd-protected {
        border-color: var(--pd-warning, #ffb74d);
      }
      .pd-audio-grab-note {
        width: 15px;
        height: 15px;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        color: var(--pd-accent, #4fc3f7);
        font-size: 15px;
      }
    `;
    document.documentElement.appendChild(style);

    audioButton = document.createElement('button');
    audioButton.type = 'button';
    audioButton.className = 'pd-audio-grab-btn pd-theme-root';

    const note = document.createElement('span');
    note.className = 'pd-audio-grab-note';
    note.textContent = '♪';

    const audioLabel = document.createElement('span');
    audioLabel.className = 'pd-audio-grab-label';
    audioLabel.textContent = PD.I18n.t('contentDownloadAudio');

    audioButton.append(note, audioLabel);

    audioButton.addEventListener('pointerenter', () => {
      if (audioHideTimer) clearTimeout(audioHideTimer);
      audioHideTimer = null;
    });

    audioButton.addEventListener('click', async event => {
      event.preventDefault();
      event.stopPropagation();

      const label = audioButton.querySelector('.pd-audio-grab-label');
      const playback = collectPlaybackState();
      if (isSpotifyProtectedPlayback(playback)) {
        label.textContent = PD.I18n.t('contentSpotifyProtected');
        audioButton.classList.add('pd-protected');

        setTimeout(() => {
          if (!audioButton?.isConnected) return;
          updateAudioButtonPresentation(playback);
        }, 3200);
        return;
      }

      label.textContent = PD.I18n.t('contentAddingAudio');

      let response = null;
      const downloadableCandidate = getDownloadableAudioCandidate();
      const preferredUrl = isSpotifyPage()
        ? (playback.activeAudioUrls || []).find(isSpotifyPreviewUrl) || ''
        : playback.activeAudioUrls?.[0] || '';

      if (downloadableCandidate?.id || preferredUrl) {
        response = await sendMessage({
          action: 'download_media_candidate',
          candidateId: downloadableCandidate?.id || '',
          preferredUrl,
          mediaType: 'audio'
        });
      } else {
        response = await sendMessage({
          action: 'download_via_ytdlp',
          url: location.href,
          filename: `${sanitizeName(document.title, 'audio')}.mp3`,
          title: document.title || 'audio',
          referer: location.href,
          audioOnly: true
        });
      }

      const ok = !!response?.success;
      label.textContent = ok ? PD.I18n.t('ytAdded') : `✗ ${response?.error || PD.I18n.t('genericError')}`;
      audioButton.classList.toggle('pd-success', ok);

      setTimeout(() => {
        if (!audioButton?.isConnected) return;
        updateAudioButtonPresentation();
      }, 2200);
    });

    (document.body || document.documentElement).appendChild(audioButton);
    return audioButton;
  }

  function updateAudioButtonPresentation(state = collectPlaybackState()) {
    const button = ensureAudioButton();
    const label = button.querySelector('.pd-audio-grab-label');
    const note = button.querySelector('.pd-audio-grab-note');
    const spotifyProtected = isSpotifyProtectedPlayback(state);

    button.classList.remove('pd-success', 'pd-protected');
    button.classList.toggle('pd-protected', spotifyProtected);
    note.textContent = spotifyProtected ? '🔒' : '♪';
    label.textContent = spotifyProtected
      ? PD.I18n.t('contentSpotifyDetected')
      : PD.I18n.t('contentDownloadAudio');
    button.title = spotifyProtected
      ? PD.I18n.t('contentSpotifyProtectedTitle', [getSpotifyTrackTitle()])
      : '';
  }

  function refreshAudioButton(state = collectPlaybackState()) {
    if (!IS_TOP_FRAME) return;
    const button = ensureAudioButton();
    updateAudioButtonPresentation(state);
    button.classList.toggle('pd-visible', canShowAudioButton(state));
  }

  function handleCandidateUpdate(message) {
    bestAudioCandidate = message.bestAudio || null;
    tabAudible = !!message.audible;
    refreshAudioButton(message.playback || collectPlaybackState());
  }

  function handlePageChange() {
    if (location.href === lastPageUrl) return;
    lastPageUrl = location.href;
    bestAudioCandidate = null;
    reportedPerformanceUrls.clear();
    void sendMessage({ action: 'media_page_changed', url: location.href });
    setTimeout(() => scanMediaElements(), 250);
  }

  PDWebExt.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message?.action === 'media_candidates_updated') {
      handleCandidateUpdate(message);
      return false;
    }

    if (message?.action === 'pd_rescan_media') {
      scanMediaElements();
      scanPerformanceEntries();
      publishPlaybackState(true);
      sendResponse?.({ success: true });
      return false;
    }

    return false;
  });

  const observer = new MutationObserver(records => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node instanceof Element) scanMediaElements(node);
      }
    }
  });

  observer.observe(document.documentElement, { childList: true, subtree: true });

  document.addEventListener('play', event => {
    if (event.target instanceof HTMLMediaElement) {
      observeMedia(event.target);
      reportMediaElement(event.target, 'dom-playing');
      publishPlaybackState(true);
    }
  }, true);

  document.addEventListener('pause', () => publishPlaybackState(true), true);
  document.addEventListener('ended', () => publishPlaybackState(true), true);

  scanMediaElements();
  scanPerformanceEntries();

  let heartbeat = 0;
  setInterval(() => {
    handlePageChange();
    scanPerformanceEntries();
    publishPlaybackState();

    // Re-announce active media periodically so a recreated Manifest V3 service
    // worker can rebuild its in-memory registry without reloading the page.
    heartbeat++;
    if (heartbeat % 6 === 0) {
      scanMediaElements();
      publishPlaybackState(true);
    }
  }, 1800);
})();
