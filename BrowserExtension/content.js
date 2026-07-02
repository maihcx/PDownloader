// ============================================================
// PDownloader Extension — Content Script
// - Hover overlay button on <video> elements
// - Magnet link interception
// - Excludes YouTube watch pages (handled by youtube_content.js)
// ============================================================

// ── Floating button styles ────────────────────────────────────────────────────
const _style = document.createElement('style');
_style.textContent = `
.pd-grab-btn {
  position: absolute;
  z-index: 2147483647;
  font-family: 'Segoe UI', system-ui, sans-serif;
  user-select: none;
  background: rgba(13, 17, 23, 0.88);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
  border: 1px solid rgba(79, 195, 247, 0.25);
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0,0,0,0.45),
              0 0 0 1px rgba(79,195,247,0.08);
  padding: 5px 12px;
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 12px;
  font-weight: 600;
  color: #e6edf3;
  cursor: pointer;
  opacity: 0;
  transition: opacity .18s, transform .1s, border-color .15s;
  pointer-events: auto;
}
.pd-grab-btn:hover {
  background: rgba(79,195,247,0.15);
  border-color: rgba(79,195,247,0.55);
  color: #fff;
}
.pd-grab-btn:active { transform: scale(0.96); }
.pd-grab-icon {
  width: 0; height: 0;
  border-left: 9px solid #4FC3F7;
  border-top: 5px solid transparent;
  border-bottom: 5px solid transparent;
  display: inline-block;
}
.pd-grab-btn.success {
  border-color: rgba(76,175,80,0.6);
  background: rgba(76,175,80,0.15);
}
.pd-grab-btn.success .pd-grab-icon {
  border-left-color: #4CAF50;
}
`;
document.head.appendChild(_style);

// ── State ─────────────────────────────────────────────────────────────────────
let _activeVideo  = null;
let _btn          = null;
let _hideTimer    = null;

// ── Skip YouTube watch (youtube_content.js handles it) ───────────────────────
function isYouTubeWatch() {
  return location.hostname.includes('youtube.com') && !location.pathname.startsWith('/shorts/');
}

// ── Build / reuse button ─────────────────────────────────────────────────────
function getBtn() {
  if (_btn) return _btn;
  _btn = document.createElement('div');
  _btn.className = 'pd-grab-btn';
  _btn.innerHTML = '<span class="pd-grab-icon"></span><span class="pd-grab-label">Tải video này</span>';

  _btn.addEventListener('mouseenter', () => { clearHide(); _btn.style.opacity = '1'; });
  _btn.addEventListener('mouseleave', scheduleHide);

  _btn.addEventListener('click', async (e) => {
    e.stopPropagation();
    if (!_activeVideo) return;

    const hostname = location.hostname;
    const isSpecial = [
      'tiktok.com','facebook.com','fb.watch','instagram.com',
      'x.com','twitter.com','vimeo.com','twitch.tv',
      'reddit.com','bilibili.com','bilibili.tv','soundcloud.com'
    ].some(h => hostname.includes(h)) || location.pathname.startsWith('/shorts/');

    let url, filename;

    if (isSpecial) {
      url      = getSiteUrl(_activeVideo);
      filename = sanitizeName(document.title) + (hostname.includes('soundcloud.com') ? '.mp3' : '.mp4');

      // Các site này không expose URL file media thật trong DOM (Facebook,
      // TikTok, Instagram... stream qua blob/DASH được ký/mã hoá theo session).
      // "url" ở đây là URL TRANG, không phải file — phải đi qua pipeline
      // yt-dlp (analyze rồi download) như YouTube, KHÔNG được gửi thẳng tới
      // /download (endpoint đó chỉ tải URL file thật, sẽ nhận về HTML và báo lỗi).
      const resp = await chrome.runtime.sendMessage({
        action: 'download_via_ytdlp', url, filename, title: document.title
      });
      showBtnFeedback(resp?.success ? '✓ Đã thêm' : ('✗ ' + (resp?.error || 'Lỗi')), resp?.success);
      return;
    }

    // Site thường: video có src trực tiếp truy cập được trong DOM.
    url = _activeVideo.currentSrc || _activeVideo.src;
    if (!url || url.startsWith('blob:')) {
      // video.src là blob: (MediaSource Extensions) — không phải URL mạng
      // thật, không tải trực tiếp được. Thử xin background.js manifest
      // .m3u8/.mpd gốc mà nó đã "nghe lén" được qua webRequest trước khi bị
      // gói thành blob (xem hlsManifestsByTab trong background.js).
      const manifest = await chrome.runtime.sendMessage({ action: 'get_hls_manifest' });
      if (manifest?.url) {
        filename = sanitizeName(document.title) + '.mp4';
        const resp = await chrome.runtime.sendMessage({
          action:   'download_via_ytdlp',
          url:      manifest.url,
          filename,
          title:    document.title,
          referer:  manifest.referer
        });
        showBtnFeedback(resp?.success ? '✓ Đã thêm' : ('✗ ' + (resp?.error || 'Lỗi')), resp?.success);
      } else {
        showBtnFeedback('⚠ Stream DRM không hỗ trợ', false);
      }
      return;
    }
    try {
      const p = new URL(url).pathname;
      const seg = p.substring(p.lastIndexOf('/') + 1);
      filename = seg.includes('.') ? seg : sanitizeName(document.title) + '.mp4';
    } catch (_) { filename = 'video.mp4'; }

    const resp = await chrome.runtime.sendMessage({
      action: 'download', url, filename, referer: location.href
    });
    showBtnFeedback(resp?.success ? '✓ Đã thêm' : '✗ Lỗi kết nối', resp?.success);
  });

  document.body.appendChild(_btn);
  return _btn;
}

function showBtnFeedback(text, ok) {
  const btn = getBtn();
  const lbl = btn.querySelector('.pd-grab-label');
  const ico = btn.querySelector('.pd-grab-icon');
  const origText = 'Tải video này';
  lbl.textContent = text;
  btn.classList.toggle('success', !!ok);
  setTimeout(() => {
    lbl.textContent = origText;
    btn.classList.remove('success');
  }, 2000);
}

function positionBtn(video) {
  const rect = video.getBoundingClientRect();
  if (rect.width < 60 || rect.height < 40) return;

  const btn = getBtn();
  const sx = window.scrollX || window.pageXOffset;
  const sy = window.scrollY || window.pageYOffset;

  const isVertical = ['tiktok.com','instagram.com','facebook.com'].some(h => location.hostname.includes(h))
    || location.pathname.startsWith('/shorts/');

  const top  = rect.top  + sy + 10;
  const left = isVertical
    ? rect.left + sx + 12
    : rect.left + sx + rect.width - 140;

  btn.style.top  = `${top}px`;
  btn.style.left = `${left}px`;
  btn.style.opacity = '1';
}

function scheduleHide() {
  clearHide();
  _hideTimer = setTimeout(() => {
    if (_btn) { _btn.style.opacity = '0'; }
  }, 1800);
}

function clearHide() {
  if (_hideTimer) { clearTimeout(_hideTimer); _hideTimer = null; }
}

// ── Site-specific URL resolution ─────────────────────────────────────────────
function getSiteUrl(video) {
  const sites = [
    { domains: ['tiktok.com'],               attr: 'href', pattern: /\/video\// },
    { domains: ['facebook.com','fb.watch'],  attr: 'href', pattern: /\/(videos|watch|reel|posts)\// },
    { domains: ['instagram.com'],            attr: 'href', pattern: /\/(p|reel)\// }
  ];

  for (const { domains, attr, pattern } of sites) {
    if (domains.some(d => location.hostname.includes(d))) {
      let el = video.parentElement;
      for (let i = 0; i < 10 && el; i++, el = el.parentElement) {
        const a = [...el.querySelectorAll('a[href]')].find(a => pattern.test(a.href));
        if (a) return a.href;
      }
    }
  }
  return location.href;
}

function sanitizeName(name) {
  return name.replace(/[\\/:*?"<>|]/g, '_').slice(0, 80);
}

// ── Mouse listeners ───────────────────────────────────────────────────────────
function initListeners() {
  if (isYouTubeWatch()) return;

  // Tránh gắn listener toàn cục (capture: true trên document) nếu trang không
  // có video nào ngay từ đầu — phần lớn trang web (text, ảnh, form...) không
  // cần overlay này, nên không có lý do để mouseover/mouseout chạy qua mọi
  // pixel di chuột trên các trang đó.
  if (!document.querySelector('video')) {
    // Một số trang load video bằng JS sau khi DOMContentLoaded (lazy load,
    // SPA...). Theo dõi DOM một lần để bật listener khi video xuất hiện,
    // rồi ngắt observer ngay — không cần observer chạy mãi mãi.
    const lateObserver = new MutationObserver(() => {
      if (document.querySelector('video')) {
        lateObserver.disconnect();
        attachVideoListeners();
      }
    });
    lateObserver.observe(document.body, { childList: true, subtree: true });
    return;
  }

  attachVideoListeners();
}

function attachVideoListeners() {
  document.addEventListener('mouseover', (e) => {
    if (isYouTubeWatch()) return;
    let v = e.target.tagName === 'VIDEO' ? e.target : null;
    if (!v) {
      let cur = e.target;
      for (let i = 0; i < 5 && cur && !v; i++, cur = cur.parentElement)
        v = cur.querySelector('video');
    }
    if (!v) return;
    _activeVideo = v;
    clearHide();
    positionBtn(v);
  }, true);

  document.addEventListener('mouseout', (e) => {
    if (isYouTubeWatch()) return;
    if (!_activeVideo) return;
    const to = e.relatedTarget;
    if (!to) { scheduleHide(); return; }
    let cur = to;
    for (let i = 0; i < 6 && cur; i++, cur = cur.parentElement) {
      if (cur === _btn || cur.contains(_activeVideo)) return;
    }
    scheduleHide();
  }, true);
}

initListeners();

// ── Magnet links ──────────────────────────────────────────────────────────────
document.addEventListener('click', (e) => {
  let t = e.target;
  while (t && t.tagName !== 'A') t = t.parentElement;
  if (t?.href?.startsWith('magnet:')) {
    e.preventDefault();
    chrome.runtime.sendMessage({ action: 'download_magnet', url: t.href });
  }
}, true);