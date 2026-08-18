const VIDEO_CONTEXT_SELECTOR = [
  '[data-video-id]',
  '[aria-label="Video player"]',
  '[data-e2e="recommend-list-item-container"]',
  '[data-e2e="browse-video"]',
  '[data-e2e="feed-video"]',
  '[data-testid*="video"]',
  'article',
  '[role="article"]'
].join(',');

let _activeVideo = null;
let _activeContextNode = null;
let _activePlayer = null;
let _activeMediaKey = '';
let _btn = null;
let _qualityPanel = null;
let _dismissedMediaKey = '';
let _hideTimer = null;
let _pointerFrame = 0;
let _lastPointerEvent = null;
let _forcePointerRefresh = false;
let _contextInvalidated = false;

function hostnameMatches(hostname, expected) {
  const host = String(hostname || '').toLowerCase().replace(/^www\./, '');
  const domain = String(expected || '').toLowerCase().replace(/^www\./, '');
  return host === domain || host.endsWith(`.${domain}`);
}

function isYouTubeHost(hostname = location.hostname) {
  const host = String(hostname || '').toLowerCase().replace(/^www\./, '');
  return host === 'youtube.com'
    || host.endsWith('.youtube.com')
    || host === 'youtube-nocookie.com'
    || host.endsWith('.youtube-nocookie.com')
    || host === 'youtu.be'
    || host.endsWith('.youtu.be');
}

function getYouTubeVideoId(rawUrl = location.href) {
  let url;
  try {
    url = new URL(rawUrl, location.href);
  } catch (_) {
    return '';
  }

  const host = url.hostname.toLowerCase().replace(/^www\./, '');
  const isYouTubeDomain = host === 'youtube.com'
    || host.endsWith('.youtube.com')
    || host === 'youtube-nocookie.com'
    || host.endsWith('.youtube-nocookie.com');

  if (host === 'youtu.be' || host.endsWith('.youtu.be')) {
    const id = url.pathname.split('/').filter(Boolean)[0] || '';
    return /^[A-Za-z0-9_-]{6,20}$/.test(id) ? id : '';
  }

  if (!isYouTubeDomain) return '';

  const queryId = url.searchParams.get('v') || '';
  if (/^[A-Za-z0-9_-]{6,20}$/.test(queryId)) return queryId;

  const pathMatch = url.pathname.match(/^\/(?:embed|shorts|live)\/([A-Za-z0-9_-]{6,20})(?:[/?]|$)/i);
  return pathMatch?.[1] || '';
}

function getCanonicalYouTubeUrl(rawUrl = location.href) {
  const videoId = getYouTubeVideoId(rawUrl);
  return videoId ? `https://www.youtube.com/watch?v=${videoId}` : '';
}

function isVimeoHost(hostname = location.hostname) {
  const host = String(hostname || '').toLowerCase().replace(/^www\./, '');
  return host === 'vimeo.com'
    || host.endsWith('.vimeo.com');
}

function getVimeoVideoInfo(rawUrl = location.href) {
  let url;
  try {
    url = new URL(rawUrl, location.href);
  } catch (_) {
    return null;
  }

  if (!isVimeoHost(url.hostname)) return null;

  const pathParts = url.pathname.split('/').filter(Boolean);
  let videoId = '';
  let unlistedHash = '';

  if (url.hostname.toLowerCase() === 'player.vimeo.com') {
    if (pathParts[0]?.toLowerCase() !== 'video') return null;
    videoId = pathParts[1] || '';
    unlistedHash = url.searchParams.get('h') || '';
  } else {
    const videoIndex = pathParts[0]?.toLowerCase() === 'video' ? 1 : 0;
    videoId = pathParts[videoIndex] || '';
    unlistedHash = pathParts[videoIndex + 1] || url.searchParams.get('h') || '';
  }

  if (!/^\d+$/.test(videoId)) return null;
  if (unlistedHash && !/^[A-Za-z0-9_-]+$/.test(unlistedHash)) {
    unlistedHash = '';
  }

  return { videoId, unlistedHash };
}

function getCanonicalVimeoUrl(rawUrl = location.href) {
  const info = getVimeoVideoInfo(rawUrl);
  if (!info) return '';

  return `https://vimeo.com/${info.videoId}`
    + (info.unlistedHash ? `/${info.unlistedHash}` : '');
}

function getEmbeddingPageReferer() {
  const referrer = String(document.referrer || '').trim();
  if (window !== window.top && /^https?:\/\//i.test(referrer)) {
    return referrer;
  }

  return '';
}

function usesDedicatedYouTubePanel() {
  const host = location.hostname.toLowerCase();
  const isRegularYouTube = host === 'youtube.com' || host.endsWith('.youtube.com');

  return window === window.top && isRegularYouTube;
}

function getBtn() {
  if (_btn) return _btn;

  if (!PD.QualityAnalyzer) {
    throw new Error('Quality analyzer is not available.');
  }

  _qualityPanel = PD.QualityAnalyzer.createPanel({
    fixed: true,
    getContext: () => resolveQualityContext(_activeVideo, _activeContextNode),
    onClose: () => {
      _dismissedMediaKey = _activeMediaKey;
      hideButton(false);
    }
  });

  _btn = _qualityPanel.element;

  _btn.addEventListener('pointerenter', () => {
    clearHide();
    showButton();
  });
  _btn.addEventListener('pointerleave', () => scheduleHide(300));

  document.body.appendChild(_btn);
  return _btn;
}

async function resolveQualityContext(video, contextNode) {
  if (!video || !video.isConnected) return null;

  const hostname = location.hostname;
  const mediaTitle = getMediaTitle(video, contextNode);
  const mediaUrl = getDirectMediaUrl(video);
  const mediaKey = getVideoMediaKey(video, contextNode);
  let url = '';
  let referer = location.href;

  if (isYouTubeHost(hostname)) {
    url = getCanonicalYouTubeUrl(location.href) || location.href;
  } else if (isVimeoHost(hostname)) {
    const embeddingReferer = getEmbeddingPageReferer();
    url = getCanonicalVimeoUrl(location.href) || getSiteUrl(video, contextNode);
    referer = embeddingReferer || location.href;
  } else {
    const siteUrl = getSiteUrl(video, contextNode);
    if (siteUrl && /^https?:\/\//i.test(siteUrl)) {
      url = siteUrl;
    }

    const shouldPreferPageUrl = [
      'tiktok.com', 'facebook.com', 'fb.watch', 'instagram.com',
      'x.com', 'twitter.com', 'twitch.tv', 'reddit.com',
      'bilibili.com', 'bilibili.tv', 'soundcloud.com'
    ].some(host => hostnameMatches(hostname, host));

    if (!shouldPreferPageUrl && (!url || url === location.href)) {
      const directUrl = video.currentSrc || video.src || '';
      if (/^https?:\/\//i.test(directUrl)) url = directUrl;
    }

    const shouldUseCapturedMedia = !url
      || url === location.href
      || url.startsWith('blob:')
      || /\.(?:m3u8|mpd)(?:$|[?#])/i.test(url);

    if (!shouldPreferPageUrl && shouldUseCapturedMedia) {
      const detected = await sendMessageSafe({
        action: 'get_media_candidates',
        mediaType: 'video',
        minScore: 45
      });

      const candidates = Array.isArray(detected?.candidates)
        ? detected.candidates.filter(candidate => /^https?:\/\//i.test(candidate?.url || ''))
        : [];
      const bestCandidate = candidates[0] || null;
      const manifestCandidates = candidates.filter(candidate =>
        candidate.kind === 'hls' || candidate.kind === 'dash');

      let candidate = bestCandidate;
      if (manifestCandidates.length) {
        let bestOrigin = '';
        try { bestOrigin = new URL(bestCandidate?.url || '').origin; } catch (_) { }
        const bestManifestKind = ['hls', 'dash'].includes(bestCandidate?.kind)
          ? bestCandidate.kind
          : '';

        const sameOriginManifests = bestOrigin
          ? manifestCandidates.filter(item => {
              try {
                return new URL(item.url).origin === bestOrigin
                  && (!bestManifestKind || item.kind === bestManifestKind);
              } catch (_) {
                return false;
              }
            })
          : manifestCandidates;

        candidate = [...(sameOriginManifests.length ? sameOriginManifests : manifestCandidates)]
          .sort((a, b) =>
            Number(b.lastSeenAt || b.foundAt || 0)
            - Number(a.lastSeenAt || a.foundAt || 0))[0];
      }

      if (candidate?.url) {
        url = candidate.url;
        referer = candidate.referer || candidate.pageUrl || location.href;
      }
    }

    if (!url || url === location.href || url.startsWith('blob:')) {
      const manifest = await sendMessageSafe({ action: 'get_hls_manifest' });
      if (manifest?.url && /^https?:\/\//i.test(manifest.url)) {
        url = manifest.url;
        referer = manifest.referer || location.href;
      }
    }

    if (!url || url.startsWith('blob:')) url = location.href;
  }

  return {
    url,
    cacheKey: `${url}|${mediaKey}|${mediaUrl}|${mediaTitle}`,
    title: mediaTitle,
    referer,
    mediaUrl,
    mediaKey,
    allowDirectFallback: !isYouTubeHost(hostname)
  };
}

async function sendMessageSafe(message) {
  if (_contextInvalidated) {
    return { success: false, error: PD.I18n.t('contentReloadPage') };
  }

  try {
    if (!PDWebExt.runtime?.id) {
      throw new Error('Extension context invalidated.');
    }
    return await PDWebExt.runtime.sendMessage(message);
  } catch (error) {
    const text = String(error?.message || error || '');
    if (/extension context invalidated|receiving end does not exist|message port closed/i.test(text)) {
      _contextInvalidated = true;
      hideButton(true);
      return { success: false, error: PD.I18n.t('contentReloadPage') };
    }
    throw error;
  }
}

function showButton() {
  const btn = getBtn();
  btn.style.display = 'flex';
  btn.style.visibility = 'visible';
  btn.style.opacity = '1';
}

function getButtonHost(video) {
  if (!(video instanceof Element)) {
    return document.body || document.documentElement;
  }

  const dialog = video.closest?.('dialog[open]');
  if (dialog) {
    return dialog.querySelector?.('.fancybox__container') || dialog;
  }

  const fullscreen = document.fullscreenElement;
  if (fullscreen instanceof Element && fullscreen.contains(video)) {
    return fullscreen;
  }

  try {
    const popover = video.closest?.(':popover-open');
    if (popover) return popover;
  } catch (_) { }

  return document.body || document.documentElement;
}

function mountButtonForVideo(video) {
  const btn = getBtn();
  const host = getButtonHost(video);

  if (host && btn.parentNode !== host) {
    host.appendChild(btn);
  }

  return btn;
}

function hideButton(clearActive = true) {
  if (_btn) {
    _btn.style.opacity = '0';
    _btn.style.visibility = 'hidden';
  }

  if (clearActive) {
    _qualityPanel?.invalidateContext?.();
    _activeVideo = null;
    _activeContextNode = null;
    _activePlayer = null;
    _activeMediaKey = '';
  }
}

function positionBtn(video) {
  if (!isRenderedVideo(video)) {
    hideButton(true);
    return;
  }

  const rect = video.getBoundingClientRect();
  const btn = mountButtonForVideo(video);

  const viewportWidth = document.documentElement.clientWidth || window.innerWidth;
  const estimatedWidth = Math.max(btn.offsetWidth || 0, 176);
  const top = clamp(rect.top + 10, 8, Math.max(8, window.innerHeight - 40));
  const preferredRight = viewportWidth - rect.right + 12;
  const right = clamp(preferredRight, 8, Math.max(8, viewportWidth - estimatedWidth - 8));

  btn.style.top = `${Math.round(top)}px`;
  btn.style.left = 'auto';
  btn.style.right = `${right}px`;
  _qualityPanel?.setDropdownAlignment('right');
  showButton();
}

function scheduleHide(delay = 450) {
  if (_hideTimer) return;
  _hideTimer = setTimeout(() => {
    _hideTimer = null;
    hideButton(true);
  }, delay);
}

function clearHide() {
  if (_hideTimer) {
    clearTimeout(_hideTimer);
    _hideTimer = null;
  }
}

function getSiteUrl(video, contextNode) {
  return PD.SiteUrlResolver?.resolve(video, contextNode || video) || location.href;
}

function getMediaTitle(video, contextNode) {
  return PD.SiteUrlResolver?.getMediaTitle?.(video, contextNode || video)
    || document.title
    || 'video';
}

function getDirectMediaUrl(video) {
  const values = [
    video?.currentSrc,
    video?.src,
    video?.getAttribute?.('src')
  ];

  try {
    for (const source of video?.querySelectorAll?.('source[src]') || []) {
      values.push(source.src, source.getAttribute('src'));
    }
  } catch (_) { }

  return values.find(value => /^https?:\/\//i.test(String(value || ''))) || '';
}

function getVideoMediaKey(video, contextNode = video) {
  if (!(video instanceof HTMLVideoElement)) return '';

  const wrapper = video.closest?.('[id^="xgwrapper-"]');
  const mediaNode = video.closest?.(
    '[data-item-id],[data-video-id],[data-aweme-id],[data-e2e="feed-video"],[data-e2e="browse-video"]'
  );
  const values = [
    wrapper?.id,
    mediaNode?.getAttribute?.('data-item-id'),
    mediaNode?.getAttribute?.('data-video-id'),
    mediaNode?.getAttribute?.('data-aweme-id'),
    mediaNode?.id,
    contextNode?.getAttribute?.('data-item-id'),
    contextNode?.getAttribute?.('data-video-id'),
    contextNode?.getAttribute?.('data-aweme-id'),
    video.currentSrc,
    video.src,
    video.getAttribute?.('src'),
    video.poster,
    video.getAttribute?.('poster')
  ].map(value => String(value || '').trim()).filter(Boolean);

  return [...new Set(values)].join('|');
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function isRenderedVideo(video) {
  if (!(video instanceof HTMLVideoElement) || !video.isConnected) return false;

  const rect = video.getBoundingClientRect();
  if (rect.width < 60 || rect.height < 40) return false;
  if (rect.bottom <= 0 || rect.right <= 0 || rect.top >= window.innerHeight || rect.left >= window.innerWidth) {
    return false;
  }

  if (!video.getClientRects().length) return false;

  const allowTransparentVideoElement = hostnameMatches(location.hostname, 'tiktok.com')
    && !!video.closest?.(
      '[data-e2e="feed-video"],[data-e2e="browse-video"],[data-e2e="recommend-list-item-container"]'
    );

  let element = video;
  for (let depth = 0; element && depth < 24; depth++, element = element.parentElement) {
    if (element.hidden
        || element.hasAttribute?.('inert')
        || element.getAttribute?.('aria-hidden') === 'true') {
      return false;
    }

    const style = getComputedStyle(element);
    const isTransparent = Number.parseFloat(style.opacity || '1') <= 0.01;
    if (style.display === 'none'
        || style.visibility === 'hidden'
        || style.visibility === 'collapse'
        || (isTransparent && !(element === video && allowTransparentVideoElement))) {
      return false;
    }

    if (element === document.body || element === document.documentElement) break;
  }

  return true;
}

function containsPoint(rect, x, y, margin = 1) {
  return x >= rect.left - margin
    && x <= rect.right + margin
    && y >= rect.top - margin
    && y <= rect.bottom + margin;
}

function addVideosFromRoot(root, candidates) {
  if (!root) return;
  if (root instanceof HTMLVideoElement) {
    candidates.add(root);
    return;
  }

  try {
    for (const video of root.querySelectorAll('video')) {
      candidates.add(video);
    }
  } catch (_) { }
}

function getVideoContextNode(target, video) {
  const targetElement = target instanceof Element ? target : null;
  const targetContext = targetElement?.closest?.(VIDEO_CONTEXT_SELECTOR);
  if (targetContext?.matches?.('[aria-label="Video player"]')) {
    return targetContext;
  }
  if (targetContext && (targetContext === video || targetContext.contains(video))) {
    return targetContext;
  }

  return video.closest?.(VIDEO_CONTEXT_SELECTOR) || video;
}

function getVideoPlayerNode(target, video, contextNode) {
  const targetElement = target instanceof Element ? target : null;
  const pointedPlayer = targetElement?.closest?.('[aria-label="Video player"]');
  if (pointedPlayer) return pointedPlayer;

  if (contextNode?.matches?.('[aria-label="Video player"]')) return contextNode;

  // Facebook renders its control layer as a sibling of <video>, not as the
  // video's ancestor. Walk to the first small media root that owns exactly one
  // video and one control layer so each feed player gets a stable identity.
  let root = video?.parentElement || null;
  for (let depth = 0; root && depth < 10; depth++, root = root.parentElement) {
    let players = [];
    let videos = [];
    try {
      players = root.querySelectorAll('[aria-label="Video player"]');
      videos = root.querySelectorAll('video');
    } catch (_) { }

    if (players.length === 1 && videos.length === 1 && videos[0] === video) {
      return players[0];
    }
  }

  return video?.closest?.('[data-video-id],article,[role="article"],[aria-posinset]')
    || contextNode
    || video;
}

function activateVideoPlayer(video, target) {
  const contextNode = getVideoContextNode(target, video);
  const player = getVideoPlayerNode(target, video, contextNode);
  const mediaKey = getVideoMediaKey(video, contextNode);

  if ((_activePlayer && player !== _activePlayer)
      || (_activeMediaKey && mediaKey !== _activeMediaKey)) {
    _qualityPanel?.invalidateContext?.();
  }

  _activeVideo = video;
  _activeContextNode = contextNode;
  _activePlayer = player;
  _activeMediaKey = mediaKey;

  return { player, mediaKey };
}

function findVideoAtPoint(x, y, target) {
  const candidates = new Set();
  const targetElement = target instanceof Element ? target : null;

  const directVideo = targetElement?.closest?.('video');
  if (directVideo) candidates.add(directVideo);

  const stack = document.elementsFromPoint?.(x, y) || (targetElement ? [targetElement] : []);
  for (const element of stack) {
    if (!(element instanceof Element)) continue;

    if (element instanceof HTMLVideoElement) candidates.add(element);

    const context = element.closest?.(VIDEO_CONTEXT_SELECTOR);
    addVideosFromRoot(context, candidates);
  }

  const targetContext = targetElement?.closest?.(VIDEO_CONTEXT_SELECTOR);
  addVideosFromRoot(targetContext, candidates);

  if (candidates.size === 0) {
    let ancestor = targetElement;
    for (let level = 0; level < 12 && ancestor; level++, ancestor = ancestor.parentElement) {
      addVideosFromRoot(ancestor, candidates);
      if (candidates.size > 0) break;
    }
  }

  const ranked = [];
  for (const video of candidates) {
    if (!isRenderedVideo(video)) continue;

    const rect = video.getBoundingClientRect();
    if (!containsPoint(rect, x, y, 2)) continue;

    let score = 0;
    if (video === directVideo) score += 1000;
    if (video.closest?.('[data-video-id]')) score += 160;
    if (!video.paused && !video.ended) score += 80;
    if (video.currentSrc || video.src) score += 30;
    if (video.readyState > 0) score += 20;

    const videoContext = video.closest?.(
      '[data-video-id],[aria-label="Video player"],article,[role="article"],[data-pagelet^="FeedUnit"],[aria-posinset]'
    );
    for (let index = 0; index < stack.length; index++) {
      const painted = stack[index];
      if (!(painted instanceof Element)) continue;

      if (painted === video || video.contains(painted)) {
        score += Math.max(300, 600 - (index * 20));
        break;
      }

      const paintedContext = painted.closest?.(
        '[data-video-id],[aria-label="Video player"],article,[role="article"],[data-pagelet^="FeedUnit"],[aria-posinset]'
      );
      if (videoContext && paintedContext === videoContext) {
        score += Math.max(180, 420 - (index * 15));
        break;
      }
    }

    const centerDistance = Math.hypot(
      x - (rect.left + rect.width / 2),
      y - (rect.top + rect.height / 2)
    );
    const area = rect.width * rect.height;
    score -= centerDistance / 100;

    ranked.push({ video, score, area });
  }

  ranked.sort((a, b) => (b.score - a.score) || (a.area - b.area));
  return ranked[0]?.video || null;
}

function getVideoUnderLastPointer() {
  if (!_lastPointerEvent) return null;

  const { clientX, clientY, target: previousTarget } = _lastPointerEvent;
  const target = document.elementFromPoint?.(clientX, clientY) || previousTarget;
  const video = findVideoAtPoint(clientX, clientY, target);

  return { video, target };
}

function queuePointerProcessing(forceRefresh = false) {
  if (forceRefresh) _forcePointerRefresh = true;
  if (!_pointerFrame) {
    _pointerFrame = requestAnimationFrame(processPointerEvent);
  }
}

function processPointerEvent() {
  _pointerFrame = 0;
  const forceRefresh = _forcePointerRefresh;
  _forcePointerRefresh = false;

  if (!_lastPointerEvent || _contextInvalidated || usesDedicatedYouTubePanel()) return;

  const pointed = getVideoUnderLastPointer();
  const target = pointed?.target || _lastPointerEvent.target;

  if (!forceRefresh && _btn && target instanceof Node && _btn.contains(target)) {
    clearHide();
    return;
  }

  const video = pointed?.video || null;
  if (!video) {
    scheduleHide(350);
    return;
  }

  const nextContext = getVideoContextNode(target, video);
  const nextMediaKey = getVideoMediaKey(video, nextContext);

  if (nextMediaKey && nextMediaKey === _dismissedMediaKey) {
    hideButton(false);
    return;
  }
  if (_dismissedMediaKey && nextMediaKey !== _dismissedMediaKey) _dismissedMediaKey = '';

  clearHide();
  activateVideoPlayer(video, target);
  positionBtn(video);
}

function initListeners() {
  if (usesDedicatedYouTubePanel()) return;

  document.addEventListener('pointermove', (event) => {
    _lastPointerEvent = {
      clientX: event.clientX,
      clientY: event.clientY,
      target: event.target
    };

    queuePointerProcessing(false);
  }, true);

  document.addEventListener('pointerdown', (event) => {
    if (_btn && event.target instanceof Node && _btn.contains(event.target)) return;

    _lastPointerEvent = {
      clientX: event.clientX,
      clientY: event.clientY,
      target: event.target
    };

    const pointed = getVideoUnderLastPointer();
    if (!pointed?.video) return;

    const nextContext = getVideoContextNode(pointed.target, pointed.video);
    const nextMediaKey = getVideoMediaKey(pointed.video, nextContext);
    if (nextMediaKey && nextMediaKey === _dismissedMediaKey) return;
    if (_dismissedMediaKey && nextMediaKey !== _dismissedMediaKey) _dismissedMediaKey = '';

    clearHide();
    activateVideoPlayer(pointed.video, pointed.target);
    positionBtn(pointed.video);
  }, true);

  document.addEventListener('pointerleave', () => scheduleHide(150), true);

  const handleMediaIdentityChange = event => {
    if (!(event.target instanceof HTMLVideoElement)) return;

    if (event.target === _activeVideo) {
      _qualityPanel?.invalidateContext?.();
      _activeMediaKey = '';
    }

    queuePointerProcessing(true);
  };

  document.addEventListener('loadstart', handleMediaIdentityChange, true);
  document.addEventListener('emptied', handleMediaIdentityChange, true);
  document.addEventListener('loadedmetadata', handleMediaIdentityChange, true);

  const reposition = () => {
    if (_activeVideo && isRenderedVideo(_activeVideo)) {
      positionBtn(_activeVideo);
    } else {
      hideButton(true);
    }
  };

  window.addEventListener('scroll', () => {
    if (_lastPointerEvent) {
      queuePointerProcessing(true);
    } else {
      reposition();
    }
  }, true);

  window.addEventListener('resize', () => {
    if (_lastPointerEvent) {
      queuePointerProcessing(true);
    } else {
      reposition();
    }
  }, { passive: true });
}

initListeners();

document.addEventListener('click', (e) => {
  let t = e.target;
  while (t && t.tagName !== 'A') t = t.parentElement;
  if (t?.href?.startsWith('magnet:')) {
    e.preventDefault();
    void sendMessageSafe({ action: 'download_magnet', url: t.href });
  }
}, true);
