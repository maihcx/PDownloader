const _themeLink = document.createElement('link');
_themeLink.rel = 'stylesheet';
_themeLink.href = PDWebExt.runtime.getURL('common/theme.css');
document.head.appendChild(_themeLink);

const _style = document.createElement('style');
_style.textContent = `
.pd-grab-btn {
  position: fixed;
  z-index: 2147483647;
  font-family: 'Segoe UI', system-ui, sans-serif;
  user-select: none;
  background: var(--pd-bg);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
  border: 1px solid var(--pd-border);
  border-radius: 8px;
  box-shadow: 0 4px 20px var(--pd-shadow),
              0 0 0 1px rgba(79,195,247,0.08);
  padding: 5px 12px;
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 12px;
  font-weight: 600;
  color: var(--pd-text);
  cursor: pointer;
  opacity: 0;
  visibility: hidden;
  transition: opacity .18s, transform .1s, border-color .15s;
  pointer-events: auto;
}
.pd-grab-btn:hover {
  background: var(--pd-accent-bg);
  border-color: var(--pd-accent);
  color: var(--pd-text);
}
.pd-grab-btn:active { transform: scale(0.96); }
.pd-grab-icon {
  width: 0; height: 0;
  border-left: 9px solid var(--pd-accent);
  border-top: 5px solid transparent;
  border-bottom: 5px solid transparent;
  display: inline-block;
}
.pd-grab-btn.success {
  border-color: var(--pd-green);
  background: var(--pd-green-bg);
}
.pd-grab-btn.success .pd-grab-icon {
  border-left-color: var(--pd-green);
}
`;
document.head.appendChild(_style);

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
let _pressedVideo = null;
let _pressedContextNode = null;
let _btn = null;
let _hideTimer = null;
let _pointerFrame = 0;
let _lastPointerEvent = null;
let _forcePointerRefresh = false;
let _contextInvalidated = false;

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

function isYouTubeWatch() {
  const host = location.hostname.toLowerCase();
  const isRegularYouTube = host === 'youtube.com' || host.endsWith('.youtube.com');

  return window === window.top
    && isRegularYouTube
    && !location.pathname.startsWith('/shorts/');
}

function getBtn() {
  if (_btn) return _btn;

  _btn = document.createElement('div');
  _btn.className = 'pd-grab-btn pd-theme-root';

  const icon = document.createElement('span');
  icon.className = 'pd-grab-icon';

  const label = document.createElement('span');
  label.className = 'pd-grab-label';
  label.textContent = PD.I18n.t('ytDownloadThisVideo');

  _btn.append(icon, label);

  _btn.addEventListener('pointerenter', () => {
    clearHide();
    showButton();
  });
  _btn.addEventListener('pointerleave', () => scheduleHide(300));

  _btn.addEventListener('pointerdown', () => {
    if (!_activeVideo || !isRenderedVideo(_activeVideo)) {
      refreshActiveVideoFromPointer();
    }

    _pressedVideo = _activeVideo;
    _pressedContextNode = _activeContextNode;
  }, true);

  _btn.addEventListener('click', async (e) => {
    e.preventDefault();
    e.stopPropagation();

    const activeVideo = isRenderedVideo(_pressedVideo)
      ? _pressedVideo
      : _activeVideo;
    const activeContextNode = activeVideo === _pressedVideo
      ? _pressedContextNode
      : _activeContextNode;

    _pressedVideo = null;
    _pressedContextNode = null;

    if (!activeVideo || !activeVideo.isConnected) return;

    const hostname = location.hostname;
    const isSpecial = [
      'tiktok.com', 'facebook.com', 'fb.watch', 'instagram.com',
      'x.com', 'twitter.com', 'vimeo.com', 'twitch.tv',
      'reddit.com', 'bilibili.com', 'bilibili.tv', 'soundcloud.com'
    ].some(h => hostname.includes(h)) || location.pathname.startsWith('/shorts/');

    try {
      let url;
      let filename;

      if (isYouTubeHost(hostname)) {
        const videoId = getYouTubeVideoId(location.href);
        const youtubeUrl = getCanonicalYouTubeUrl(location.href);

        if (!youtubeUrl) {
          showBtnFeedback(PD.I18n.t('genericError'), false);
          return;
        }

        const rawTitle = String(document.title || '').trim();
        const mediaTitle = rawTitle && !/^youtube$/i.test(rawTitle)
          ? rawTitle
          : `YouTube_${videoId}`;
        filename = sanitizeName(mediaTitle) + '.mp4';

        const resp = await sendMessageSafe({
          action: 'download_via_ytdlp',
          url: youtubeUrl,
          filename,
          title: mediaTitle,
          referer: location.href
        });

        showBtnFeedback(
          resp?.success ? PD.I18n.t('ytAdded') : ('✗ ' + (resp?.error || PD.I18n.t('genericError'))),
          resp?.success
        );
        return;
      }

      if (isSpecial) {
        url = getSiteUrl(activeVideo, activeContextNode);
        const mediaTitle = getMediaTitle(activeVideo, activeContextNode);
        filename = sanitizeName(mediaTitle) + (hostname.includes('soundcloud.com') ? '.mp3' : '.mp4');

        const detected = await sendMessageSafe({
          action: 'get_best_media_candidate',
          mediaType: hostname.includes('soundcloud.com') ? 'audio' : 'video',
          minScore: 80
        });
        const candidate = detected?.candidate || null;
        const isTikTok = hostname.includes('tiktok.com');
        const isStrongNetworkCandidate = candidate?.source === 'network'
          && candidate?.kind === 'direct'
          && !candidate?.likelySegment;
        const shouldUseCandidate = candidate?.id && (
          candidate.kind === 'hls'
          || candidate.kind === 'dash'
          || !isSpecificMediaPageUrl(url, hostname)
          || (isTikTok && isStrongNetworkCandidate)
        );
        let candidateTried = false;

        if (shouldUseCandidate) {
          candidateTried = true;
          const resp = await sendMessageSafe({
            action: 'download_media_candidate',
            candidateId: candidate.id,
            mediaType: hostname.includes('soundcloud.com') ? 'audio' : 'video'
          });

          showBtnFeedback(
            resp?.success ? PD.I18n.t('ytAdded') : ('✗ ' + (resp?.error || PD.I18n.t('genericError'))),
            resp?.success
          );
          if (resp?.success) return;
        }

        const resp = await sendMessageSafe({
          action: 'download_via_ytdlp',
          url,
          filename,
          title: mediaTitle,
          referer: location.href,
          audioOnly: hostname.includes('soundcloud.com')
        });

        if (!resp?.success && candidate?.id && !candidateTried) {
          const fallback = await sendMessageSafe({
            action: 'download_media_candidate',
            candidateId: candidate.id,
            mediaType: hostname.includes('soundcloud.com') ? 'audio' : 'video'
          });

          if (fallback?.success) {
            showBtnFeedback(PD.I18n.t('ytAdded'), true);
            return;
          }
        }

        showBtnFeedback(
          resp?.success ? PD.I18n.t('ytAdded') : ('✗ ' + (resp?.error || PD.I18n.t('genericError'))),
          resp?.success
        );
        return;
      }

      const detected = await sendMessageSafe({
        action: 'get_best_media_candidate',
        mediaType: 'video',
        minScore: 70
      });
      if (detected?.candidate?.id) {
        const resp = await sendMessageSafe({
          action: 'download_media_candidate',
          candidateId: detected.candidate.id,
          mediaType: 'video'
        });

        showBtnFeedback(
          resp?.success ? PD.I18n.t('ytAdded') : ('✗ ' + (resp?.error || PD.I18n.t('genericError'))),
          resp?.success
        );
        if (resp?.success) return;
      }

      url = activeVideo.currentSrc || activeVideo.src;
      const isPlaceholderSrc = !url
        || url.startsWith('blob:')
        || /(^|[\/_-])(blank|dummy|placeholder|empty)([\/_.-]|$)/i.test(url)
        || (isFinite(activeVideo.duration) && activeVideo.duration > 0 && activeVideo.duration < 2);

      if (isPlaceholderSrc) {
        const manifest = await sendMessageSafe({ action: 'get_hls_manifest' });
        if (manifest?.url) {
          filename = sanitizeName(document.title) + '.mp4';
          const resp = await sendMessageSafe({
            action: 'download_via_ytdlp',
            url: manifest.url,
            filename,
            title: document.title,
            referer: manifest.referer
          });
          showBtnFeedback(
            resp?.success ? PD.I18n.t('ytAdded') : ('✗ ' + (resp?.error || PD.I18n.t('genericError'))),
            resp?.success
          );
        } else {
          showBtnFeedback(PD.I18n.t('contentDrmUnsupported'), false);
        }
        return;
      }

      try {
        const p = new URL(url).pathname;
        const seg = p.substring(p.lastIndexOf('/') + 1);
        filename = seg.includes('.') ? seg : sanitizeName(document.title) + '.mp4';
      } catch (_) {
        filename = 'video.mp4';
      }

      const resp = await sendMessageSafe({
        action: 'download',
        url,
        filename,
        referer: location.href
      });
      showBtnFeedback(
        resp?.success ? PD.I18n.t('ytAdded') : PD.I18n.t('contentConnError'),
        resp?.success
      );
    } catch (error) {
      showBtnFeedback('✗ ' + (error?.message || PD.I18n.t('genericError')), false);
    }
  });

  document.body.appendChild(_btn);
  return _btn;
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

function showBtnFeedback(text, ok) {
  const btn = getBtn();
  const lbl = btn.querySelector('.pd-grab-label');
  const origText = PD.I18n.t('ytDownloadThisVideo');
  lbl.textContent = text;
  btn.classList.toggle('success', !!ok);
  showButton();

  setTimeout(() => {
    if (!lbl.isConnected) return;
    lbl.textContent = origText;
    btn.classList.remove('success');
  }, 2000);
}

function showButton() {
  const btn = getBtn();
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
    _activeVideo = null;
    _activeContextNode = null;
    _pressedVideo = null;
    _pressedContextNode = null;
  }
}

function positionBtn(video) {
  if (!isRenderedVideo(video)) {
    hideButton(true);
    return;
  }

  const rect = video.getBoundingClientRect();
  const btn = mountButtonForVideo(video);
  const isVertical = ['tiktok.com', 'instagram.com', 'facebook.com'].some(h => location.hostname.includes(h))
    || location.pathname.startsWith('/shorts/');

  const estimatedWidth = Math.max(btn.offsetWidth || 0, 132);
  const top = clamp(rect.top + 10, 8, Math.max(8, window.innerHeight - 40));
  const preferredLeft = isVertical
    ? rect.left + 12
    : rect.right - estimatedWidth - 12;
  const left = clamp(preferredLeft, 8, Math.max(8, window.innerWidth - estimatedWidth - 8));

  btn.style.top = `${Math.round(top)}px`;
  btn.style.left = `${Math.round(left)}px`;
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

function isSpecificMediaPageUrl(rawUrl, hostname = location.hostname) {
  try {
    const url = new URL(rawUrl, location.href);
    const path = url.pathname;
    const host = String(hostname || url.hostname).toLowerCase();

    if (host.includes('tiktok.com')) return /\/@[^/]+\/video\/\d+/i.test(path);
    if (host.includes('instagram.com')) return /^\/(?:reel|p|tv)\/[^/]+/i.test(path);
    if (host.includes('facebook.com') || host.includes('fb.watch')) {
      return !!url.searchParams.get('v')
        || /\/(?:reel|videos|watch)\//i.test(path)
        || /\/video\.php$/i.test(path);
    }
    if (host.includes('x.com') || host.includes('twitter.com')) return /\/status\/\d+/i.test(path);
    if (host.includes('reddit.com')) return /\/comments\/[A-Za-z0-9]+/i.test(path);
    if (host.includes('vimeo.com')) return /\/\d+(?:$|\/)/.test(path);
    if (host.includes('bilibili.')) return /\/video\//i.test(path);
    if (host.includes('soundcloud.com')) return path.split('/').filter(Boolean).length >= 2;

    return rawUrl !== location.href;
  } catch (_) {
    return false;
  }
}

function getMediaTitle(video, contextNode) {
  return PD.SiteUrlResolver?.getMediaTitle?.(video, contextNode || video)
    || document.title
    || 'video';
}

function sanitizeName(name) {
  const sanitized = String(name || 'video')
    .replace(/[\\/:*?"<>|]/g, '_')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 80);
  return sanitized || 'video';
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

  const style = getComputedStyle(video);
  return style.display !== 'none'
    && style.visibility !== 'hidden'
    && Number.parseFloat(style.opacity || '1') > 0.01;
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
  if (targetContext && (targetContext === video || targetContext.contains(video))) {
    return targetContext;
  }

  return video.closest?.(VIDEO_CONTEXT_SELECTOR) || video;
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

function refreshActiveVideoFromPointer() {
  const pointed = getVideoUnderLastPointer();
  if (!pointed?.video) return false;

  _activeVideo = pointed.video;
  _activeContextNode = getVideoContextNode(pointed.target, pointed.video);
  return true;
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

  if (!_lastPointerEvent || _contextInvalidated || isYouTubeWatch()) return;

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

  clearHide();
  _activeVideo = video;
  _activeContextNode = getVideoContextNode(target, video);
  positionBtn(video);
}

function initListeners() {
  if (isYouTubeWatch()) return;

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

    clearHide();
    _activeVideo = pointed.video;
    _activeContextNode = getVideoContextNode(pointed.target, pointed.video);
    positionBtn(pointed.video);
  }, true);

  document.addEventListener('pointerleave', () => scheduleHide(150), true);

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
