const _themeLink = document.createElement('link');
_themeLink.rel = 'stylesheet';
_themeLink.href = chrome.runtime.getURL('common/theme.css');
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
let _btn = null;
let _hideTimer = null;
let _pointerFrame = 0;
let _lastPointerEvent = null;
let _contextInvalidated = false;

function isYouTubeWatch() {
  return location.hostname.includes('youtube.com') && !location.pathname.startsWith('/shorts/');
}

function getBtn() {
  if (_btn) return _btn;

  _btn = document.createElement('div');
  _btn.className = 'pd-grab-btn pd-theme-root';
  _btn.innerHTML = `<span class="pd-grab-icon"></span><span class="pd-grab-label">${PD.I18n.t('ytDownloadThisVideo')}</span>`;

  _btn.addEventListener('pointerenter', () => {
    clearHide();
    showButton();
  });
  _btn.addEventListener('pointerleave', () => scheduleHide(300));

  _btn.addEventListener('click', async (e) => {
    e.preventDefault();
    e.stopPropagation();

    const activeVideo = _activeVideo;
    const activeContextNode = _activeContextNode;
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

      if (isSpecial) {
        url = getSiteUrl(activeVideo, activeContextNode);
        const mediaTitle = getMediaTitle(activeVideo, activeContextNode);
        filename = sanitizeName(mediaTitle) + (hostname.includes('soundcloud.com') ? '.mp3' : '.mp4');

        const resp = await sendMessageSafe({
          action: 'download_via_ytdlp',
          url,
          filename,
          title: mediaTitle
        });

        showBtnFeedback(
          resp?.success ? PD.I18n.t('ytAdded') : ('✗ ' + (resp?.error || PD.I18n.t('genericError'))),
          resp?.success
        );
        return;
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
    if (!chrome.runtime?.id) {
      throw new Error('Extension context invalidated.');
    }
    return await chrome.runtime.sendMessage(message);
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

function hideButton(clearActive = true) {
  if (_btn) {
    _btn.style.opacity = '0';
    _btn.style.visibility = 'hidden';
  }

  if (clearActive) {
    _activeVideo = null;
    _activeContextNode = null;
  }
}

function positionBtn(video) {
  if (!isRenderedVideo(video)) {
    hideButton(true);
    return;
  }

  const rect = video.getBoundingClientRect();
  const btn = getBtn();
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

  // Fallback for players whose transparent controls are not nested under the video container.
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

function processPointerEvent(event) {
  _pointerFrame = 0;
  if (!_lastPointerEvent || _contextInvalidated || isYouTubeWatch()) return;

  const { clientX, clientY, target } = _lastPointerEvent;
  if (_btn && target instanceof Node && _btn.contains(target)) {
    clearHide();
    return;
  }

  const video = findVideoAtPoint(clientX, clientY, target);
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

    if (!_pointerFrame) {
      _pointerFrame = requestAnimationFrame(processPointerEvent);
    }
  }, true);

  document.addEventListener('pointerleave', () => scheduleHide(150), true);

  const reposition = () => {
    if (_activeVideo && isRenderedVideo(_activeVideo)) {
      positionBtn(_activeVideo);
    } else {
      hideButton(true);
    }
  };

  window.addEventListener('scroll', reposition, true);
  window.addEventListener('resize', reposition, { passive: true });
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
