(function (root) {
  const PD = root.PD || (root.PD = {});
  const Registry = PD.MediaCandidateRegistry;

  const AUDIO_EXTENSIONS = new Set(['mp3', 'm4a', 'aac', 'flac', 'wav', 'ogg', 'oga', 'opus', 'weba', 'wma', 'alac']);
  const VIDEO_EXTENSIONS = new Set(['mp4', 'webm', 'mkv', 'mov', 'm4v', 'avi', 'flv', 'wmv', '3gp', 'mpeg', 'mpg', 'ogv']);
  const MANIFEST_EXTENSIONS = new Set(['m3u8', 'mpd']);
  const SEGMENT_EXTENSIONS = new Set(['m4s', 'cmfv', 'cmfa', 'm4f', 'part', 'frag']);
  const requestMetadata = new Map();
  const playbackStateByTab = new Map(); // tabId -> Map(frameId, state)
  const notifyTimers = new Map();

  function headerValue(headers, name) {
    const wanted = String(name || '').toLowerCase();
    return headers?.find(header => String(header.name || '').toLowerCase() === wanted)?.value || '';
  }

  function collectForwardableRequestHeaders(headers) {
    const names = [
      'origin',
      'authorization',
      'accept',
      'accept-language',
      'user-agent'
    ];
    const result = {};

    for (const name of names) {
      const value = headerValue(headers, name);
      if (!value) continue;

      const canonicalName = name.split('-')
        .map(part => part.charAt(0).toUpperCase() + part.slice(1))
        .join('-');
      result[canonicalName] = value;
    }

    return result;
  }

  function extensionOf(value) {
    const raw = String(value || '');
    if (!raw) return '';

    let path = raw;
    try { path = decodeURIComponent(new URL(raw).pathname); } catch (_) { }

    const file = path.split(/[?#]/)[0].slice(path.lastIndexOf('/') + 1);
    const match = file.match(/\.([A-Za-z0-9]{1,8})$/);
    return match?.[1]?.toLowerCase() || '';
  }

  function normalizeComparableUrl(url) {
    try {
      const parsed = new URL(String(url || ''));
      parsed.hash = '';
      return parsed.href;
    } catch (_) {
      return String(url || '').split('#')[0];
    }
  }

  function activeUrlsForMediaType(playback, mediaType) {
    if (mediaType === 'audio') return playback?.activeAudioUrls || [];
    if (mediaType === 'video') return playback?.activeVideoUrls || [];
    return [
      ...(playback?.activeAudioUrls || []),
      ...(playback?.activeVideoUrls || [])
    ];
  }

  function orderCandidatesForPlayback(tabId, candidates, mediaType = '') {
    const playback = aggregatePlaybackState(tabId);
    const activeUrls = new Set(
      activeUrlsForMediaType(playback, mediaType)
        .map(normalizeComparableUrl)
        .filter(Boolean)
    );

    if (!activeUrls.size) return candidates;

    return [...candidates].sort((a, b) => {
      const aActive = activeUrls.has(normalizeComparableUrl(a.url)) ? 1 : 0;
      const bActive = activeUrls.has(normalizeComparableUrl(b.url)) ? 1 : 0;
      if (aActive !== bActive) return bActive - aActive;
      return (b.score - a.score) || (b.lastSeenAt - a.lastSeenAt);
    });
  }

  function getCandidatesForPlayback(tabId, options = {}) {
    const candidates = Registry.getAll(tabId, options);
    return orderCandidatesForPlayback(tabId, candidates, options.mediaType || '');
  }

  function getBestCandidate(tabId, options = {}) {
    return getCandidatesForPlayback(tabId, options)[0] || null;
  }

  function classify(url, mime, filename = '', requestType = '', fallbackMediaType = 'video') {
    const normalizedMime = String(mime || '').split(';')[0].trim().toLowerCase();
    const extension = extensionOf(filename) || extensionOf(url);

    if (MANIFEST_EXTENSIONS.has(extension) || /(?:mpegurl|dash\+xml)/i.test(normalizedMime)) {
      return {
        mediaType: 'manifest',
        kind: extension === 'mpd' || /dash\+xml/i.test(normalizedMime) ? 'dash' : 'hls',
        extension,
        inferred: false
      };
    }

    if (normalizedMime.startsWith('audio/') || AUDIO_EXTENSIONS.has(extension)) {
      return { mediaType: 'audio', kind: 'direct', extension, inferred: false };
    }

    if (normalizedMime.startsWith('video/') || VIDEO_EXTENSIONS.has(extension)) {
      return { mediaType: 'video', kind: 'direct', extension, inferred: false };
    }

    // Some protected players (notably short-video sites) respond to a genuine
    // <video>/<audio> request with application/octet-stream or without a useful
    // MIME type. requestType=media is still a strong browser-level signal, so
    // keep that request as a candidate instead of losing the real signed URL.
    if (requestType === 'media'
        && (!normalizedMime
          || normalizedMime === 'application/octet-stream'
          || normalizedMime === 'binary/octet-stream')) {
      const value = String(url || '').toLowerCase();
      const looksAudio = /(?:^|[\/_?&=.-])(audio|music|sound|aac|m4a|mp3|opus)(?:[\/_?&=.-]|$)/i.test(value);
      const mediaType = looksAudio ? 'audio' : fallbackMediaType === 'audio' ? 'audio' : 'video';
      return { mediaType, kind: 'direct', extension, inferred: true };
    }

    return null;
  }

  function isLikelySegment(url, extension, size, requestType) {
    if (SEGMENT_EXTENSIONS.has(extension)) return true;

    const value = String(url || '').toLowerCase();
    if (/\/(?:segment|segments|chunk|chunks|frag|fragments|part)[/_-]/i.test(value)) return true;
    if (/[?&](?:seg(?:ment)?|frag(?:ment)?|chunk|part|sq|range)=/i.test(value)) return true;
    if (requestType !== 'media' && extension === 'ts') return true;
    if (size > 0 && size < 96 * 1024 && /(?:segment|frag|chunk|part|m4s|cmf)/i.test(value)) return true;

    return false;
  }

  function buildScore({ mediaType, kind, extension, mime, size, filename, requestType, likelySegment, source }) {
    let score = 0;

    if (kind === 'hls' || kind === 'dash') score += 150;
    if (String(mime || '').startsWith('video/')) score += 115;
    if (String(mime || '').startsWith('audio/')) score += 115;
    if (VIDEO_EXTENSIONS.has(extension) || AUDIO_EXTENSIONS.has(extension)) score += 40;
    if (requestType === 'media') score += 35;
    else if (requestType === 'xmlhttprequest' || requestType === 'other') score += 12;
    if (filename) score += 20;
    if (size >= 50 * 1024 * 1024) score += 35;
    else if (size >= 5 * 1024 * 1024) score += 25;
    else if (size >= 512 * 1024) score += 12;
    else if (size > 0 && size < 64 * 1024) score -= 30;
    if (source === 'dom-playing') score += 55;
    if (source === 'performance') score -= 10;
    if (mediaType === 'manifest') score += 15;
    if (likelySegment) score -= 140;

    return score;
  }

  function registerNetworkCandidate(details) {
    if (details.tabId < 0) return null;

    const contentType = headerValue(details.responseHeaders, 'content-type');
    const contentDisposition = headerValue(details.responseHeaders, 'content-disposition');
    const filename = contentDisposition ? PD.Utils.parseContentDisposition(contentDisposition) : '';
    const playback = aggregatePlaybackState(details.tabId);
    const fallbackMediaType = playback?.playingAudio && !playback?.playingVideo
      ? 'audio'
      : 'video';
    const classification = classify(
      details.url,
      contentType,
      filename,
      details.type,
      fallbackMediaType
    );
    if (!classification) return null;

    const contentLength = Number.parseInt(headerValue(details.responseHeaders, 'content-length'), 10) || 0;
    const metadata = requestMetadata.get(details.requestId) || {};
    const likelySegment = isLikelySegment(
      details.url,
      classification.extension,
      contentLength,
      details.type
    );

    const candidate = Registry.register(details.tabId, {
      url: details.url,
      mediaType: classification.mediaType,
      kind: classification.kind,
      mime: contentType,
      extension: classification.extension,
      filename,
      size: contentLength,
      referer: metadata.referer || details.documentUrl || details.initiator || '',
      pageUrl: details.documentUrl || metadata.referer || details.initiator || '',
      source: 'network',
      requestType: details.type,
      likelySegment,
      requestHeaders: { ...(metadata.requestHeaders || {}) },
      score: buildScore({
        mediaType: classification.mediaType,
        kind: classification.kind,
        extension: classification.extension,
        mime: contentType,
        size: contentLength,
        filename,
        requestType: details.type,
        likelySegment,
        source: 'network'
      }) + (classification.inferred ? 70 : 0)
    });

    if (classification.kind === 'hls' || classification.kind === 'dash') {
      PD.State.hlsManifestsByTab.set(details.tabId, candidate);
    }

    return candidate;
  }

  function scheduleNotify(tabId) {
    if (notifyTimers.has(tabId)) return;

    notifyTimers.set(tabId, setTimeout(async () => {
      notifyTimers.delete(tabId);

      const bestAudio = getBestCandidate(tabId, { mediaType: 'audio', minScore: 45 });
      const bestVideo = getBestCandidate(tabId, { mediaType: 'video', minScore: 45 });
      let audible = false;

      try {
        const tab = await PDWebExt.tabs.get(tabId);
        audible = !!tab?.audible;
      } catch (_) { }

      PDWebExt.tabs.sendMessage(tabId, {
        action: 'media_candidates_updated',
        bestAudio,
        bestVideo,
        audible,
        playback: aggregatePlaybackState(tabId)
      }).catch(() => {});
    }, 120));
  }

  function registerContentCandidate(tabId, candidate) {
    if (!candidate?.url) return null;

    const classification = classify(candidate.url, candidate.mime || '', '', 'media', candidate.mediaType || 'video');
    const mediaType = candidate.mediaType || classification?.mediaType || 'unknown';
    const kind = candidate.kind || classification?.kind || 'direct';
    const extension = candidate.extension || classification?.extension || extensionOf(candidate.url);
    const likelySegment = isLikelySegment(candidate.url, extension, Number(candidate.size) || 0, 'media');
    const source = candidate.source || 'dom';

    return Registry.register(tabId, {
      ...candidate,
      mediaType,
      kind,
      extension,
      likelySegment,
      source,
      score: Number.isFinite(candidate.score)
        ? candidate.score
        : buildScore({
            mediaType,
            kind,
            extension,
            mime: candidate.mime || '',
            size: Number(candidate.size) || 0,
            filename: candidate.filename || '',
            requestType: 'media',
            likelySegment,
            source
          })
    });
  }

  function aggregatePlaybackState(tabId) {
    const frames = playbackStateByTab.get(tabId);
    if (!frames) return null;

    const result = {
      playingAudio: false,
      playingVideo: false,
      visibleVideo: false,
      activeAudioUrls: [],
      activeVideoUrls: [],
      pageUrl: '',
      updatedAt: 0
    };

    const activeAudioUrls = new Set();
    const activeVideoUrls = new Set();

    for (const state of frames.values()) {
      result.playingAudio ||= !!state.playingAudio;
      result.playingVideo ||= !!state.playingVideo;
      result.visibleVideo ||= !!state.visibleVideo;
      for (const url of state.activeAudioUrls || []) activeAudioUrls.add(url);
      for (const url of state.activeVideoUrls || []) activeVideoUrls.add(url);
      if (!result.pageUrl && state.pageUrl) result.pageUrl = state.pageUrl;
      result.updatedAt = Math.max(result.updatedAt, state.updatedAt || 0);
    }

    result.activeAudioUrls = [...activeAudioUrls];
    result.activeVideoUrls = [...activeVideoUrls];
    return result;
  }

  function updatePlaybackState(tabId, frameId, state) {
    if (tabId < 0) return;

    let frames = playbackStateByTab.get(tabId);
    if (!frames) {
      frames = new Map();
      playbackStateByTab.set(tabId, frames);
    }

    frames.set(Number.isInteger(frameId) ? frameId : 0, {
      playingAudio: !!state?.playingAudio,
      playingVideo: !!state?.playingVideo,
      visibleVideo: !!state?.visibleVideo,
      activeAudioUrls: Array.isArray(state?.activeAudioUrls)
        ? state.activeAudioUrls.filter(url => /^https?:/i.test(String(url || ''))).slice(0, 8)
        : [],
      activeVideoUrls: Array.isArray(state?.activeVideoUrls)
        ? state.activeVideoUrls.filter(url => /^https?:/i.test(String(url || ''))).slice(0, 8)
        : [],
      pageUrl: state?.pageUrl || '',
      updatedAt: Date.now()
    });
    scheduleNotify(tabId);
  }

  function init() {
    void Registry.restoreSession?.();

    PDWebExt.webRequest.onBeforeRequest.addListener(
      details => {
        if (details.type === 'main_frame' && details.tabId >= 0) {
          Registry.clear(details.tabId);
          PD.State.hlsManifestsByTab.delete(details.tabId);
          playbackStateByTab.delete(details.tabId);
        }
      },
      { urls: ['<all_urls>'] }
    );

    PDWebExt.webRequest.onBeforeSendHeaders.addListener(
      details => {
        if (details.tabId < 0) return;

        requestMetadata.set(details.requestId, {
          referer: headerValue(details.requestHeaders, 'referer') || details.documentUrl || details.initiator || '',
          requestHeaders: collectForwardableRequestHeaders(details.requestHeaders),
          timestamp: Date.now()
        });
      },
      { urls: ['<all_urls>'] },
      PDWebExtCompat.webRequestExtraInfoSpec('requestHeaders', 'extraHeaders')
    );

    PDWebExt.webRequest.onHeadersReceived.addListener(
      details => {
        registerNetworkCandidate(details);
        requestMetadata.delete(details.requestId);
      },
      { urls: ['<all_urls>'] },
      PDWebExtCompat.webRequestExtraInfoSpec('responseHeaders', 'extraHeaders')
    );

    PDWebExt.webRequest.onErrorOccurred.addListener(
      details => requestMetadata.delete(details.requestId),
      { urls: ['<all_urls>'] }
    );

    PDWebExt.tabs.onRemoved.addListener(tabId => {
      Registry.clear(tabId);
      playbackStateByTab.delete(tabId);
      const timer = notifyTimers.get(tabId);
      if (timer) clearTimeout(timer);
      notifyTimers.delete(tabId);
    });

    PDWebExt.tabs.onUpdated.addListener((tabId, changeInfo) => {
      if (typeof changeInfo.audible === 'boolean') scheduleNotify(tabId);
    });

    Registry.onChange((tabId, candidate) => {
      if ((candidate?.score || 0) >= 45 && !candidate?.likelySegment) scheduleNotify(tabId);
    });

    setInterval(() => {
      const cutoff = Date.now() - 60 * 1000;
      for (const [requestId, metadata] of requestMetadata) {
        if ((metadata.timestamp || 0) < cutoff) requestMetadata.delete(requestId);
      }
    }, 30 * 1000);
  }

  PD.MediaCapture = {
    init,
    classify,
    registerContentCandidate,
    updatePlaybackState,
    getPlaybackState(tabId) {
      return aggregatePlaybackState(tabId);
    },
    getCandidatesForPlayback,
    getBestCandidate
  };
})(self);
