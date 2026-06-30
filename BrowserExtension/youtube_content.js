// ============================================================
// PDownloader Extension — YouTube Content Script
// Injects an IDM-style panel on youtube.com/watch & /shorts/
// ============================================================

const _yt_style = document.createElement('style');
_yt_style.textContent = `
.pd-yt-panel {
  position: absolute;
  top: 12px; right: 12px;
  z-index: 99999;
  font-family: 'Segoe UI', system-ui, sans-serif;
  user-select: none;
  background: rgba(13, 17, 23, 0.88);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border: 1px solid rgba(79,195,247,0.22);
  border-radius: 10px;
  box-shadow: 0 6px 28px rgba(0,0,0,0.5),
              0 0 0 1px rgba(79,195,247,0.07);
  padding: 4px 6px;
  display: flex;
  align-items: center;
  height: 36px;
  box-sizing: border-box;
  gap: 2px;
}
.pd-yt-panel.shorts { right: auto; left: 12px; }

.pd-yt-main-btn {
  display: flex; align-items: center; gap: 8px;
  background: transparent; border: none;
  color: #e6edf3; font-size: 13px; font-weight: 600;
  font-family: inherit; padding: 0 10px;
  cursor: pointer; height: 100%;
  border-radius: 7px; transition: background .18s, color .15s;
  box-sizing: border-box;
}
.pd-yt-main-btn:hover  { background: rgba(79,195,247,0.15); color:#fff; }
.pd-yt-main-btn:active { transform: scale(0.97); }

.pd-yt-icon {
  width: 0; height: 0;
  border-left: 10px solid #4FC3F7;
  border-top: 6px solid transparent;
  border-bottom: 6px solid transparent;
  display: inline-block;
  transition: border-left-color .15s;
}

.pd-yt-sep {
  width: 1px; height: 18px;
  background: rgba(255,255,255,0.12); margin: 0 2px;
}

.pd-yt-ctrl-btn {
  background: transparent; border: none;
  color: #8b949e; font-size: 13px; padding: 0 8px;
  cursor: pointer; display: flex; align-items: center;
  justify-content: center; height: 100%;
  border-radius: 7px; transition: background .18s, color .15s;
  font-weight: 600; box-sizing: border-box;
}
.pd-yt-ctrl-btn:hover { background: rgba(255,255,255,0.08); color: #e6edf3; }

/* Dropdown */
.pd-yt-dropdown {
  position: absolute;
  top: calc(100% + 8px); right: 0;
  width: 460px; max-height: 380px;
  background: rgba(13,17,23,0.95);
  backdrop-filter: blur(20px);
  border: 1px solid rgba(79,195,247,0.18);
  border-radius: 12px;
  box-shadow: 0 16px 48px rgba(0,0,0,0.7);
  padding: 10px; display: none;
  flex-direction: column; gap: 6px;
  z-index: 100000; overflow: hidden;
}
.pd-yt-dropdown.open { display: flex; }
.pd-yt-panel.shorts .pd-yt-dropdown { right: auto; left: 0; }

.pd-yt-search {
  width: 100%; background: rgba(255,255,255,0.05);
  border: 1px solid rgba(79,195,247,0.2);
  color: #e6edf3; padding: 7px 12px;
  border-radius: 8px; font-size: 13px;
  font-family: inherit; outline: none;
  transition: border-color .2s;
  box-sizing: border-box;
}
.pd-yt-search:focus { border-color: #4FC3F7; background: rgba(79,195,247,0.06); }
.pd-yt-search::placeholder { color: #8b949e; }

.pd-yt-filters {
  display: flex; gap: 6px;
  padding-bottom: 8px;
  border-bottom: 1px solid rgba(255,255,255,0.08);
  flex-shrink: 0;
}
.pd-yt-filter-btn {
  background: rgba(255,255,255,0.04);
  border: 1px solid rgba(255,255,255,0.1);
  color: #8b949e; padding: 5px 12px;
  border-radius: 6px; font-size: 12px; font-weight: 600;
  cursor: pointer; font-family: inherit; transition: all .18s;
}
.pd-yt-filter-btn:hover { border-color: rgba(79,195,247,0.4); color: #e6edf3; }
.pd-yt-filter-btn.active {
  background: rgba(79,195,247,0.15);
  border-color: #4FC3F7; color: #fff;
}

.pd-yt-list { display: flex; flex-direction: column; gap: 2px; overflow-y: auto; flex: 1; }

.pd-yt-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 8px 14px; color: #c9d1d9; font-size: 13px;
  cursor: pointer; border-radius: 8px; transition: background .15s;
}
.pd-yt-item:hover { background: rgba(79,195,247,0.1); color: #fff; }
.pd-yt-item .pd-yt-size {
  color: #4FC3F7; font-size: 12px; font-weight: 700; margin-left: 12px; flex-shrink: 0;
}

.pd-yt-spinner {
  width: 14px; height: 14px;
  border: 2px solid rgba(255,255,255,0.15);
  border-top-color: #4FC3F7;
  border-radius: 50%;
  animation: pd-spin .6s linear infinite; display: inline-block;
}
@keyframes pd-spin { to { transform: rotate(360deg); } }

.pd-yt-toast {
  position: absolute; top: calc(100% + 8px); right: 0;
  background: rgba(76,175,80,0.92);
  border: 1px solid rgba(76,175,80,0.3);
  color: #fff; font-size: 13px; padding: 7px 16px;
  border-radius: 8px; white-space: nowrap;
  box-shadow: 0 6px 20px rgba(0,0,0,0.5);
  animation: pd-toast-in 2.8s forwards; pointer-events: none; z-index: 100001;
}
.pd-yt-toast.err { background: rgba(244,67,54,0.92); border-color: rgba(244,67,54,0.3); }
@keyframes pd-toast-in {
  0%   { opacity:0; transform: translateY(-4px); }
  10%  { opacity:1; transform: translateY(0); }
  88%  { opacity:1; }
  100% { opacity:0; }
}
`;
document.head.appendChild(_yt_style);

// ── State ─────────────────────────────────────────────────────────────────────
let _formatsCache = {};
let _currentVid   = '';
let _prefetchProm = null;

// ── Helpers ───────────────────────────────────────────────────────────────────
function getVideoId() {
  if (location.pathname.startsWith('/shorts/')) return location.pathname.split('/')[2] || '';
  return new URLSearchParams(location.search).get('v') || '';
}

function isShorts() { return location.pathname.startsWith('/shorts/'); }

function prefetchFormats(vid) {
  if (_formatsCache[vid] || _prefetchProm) return;
  const url = isShorts()
    ? `https://www.youtube.com/shorts/${vid}`
    : `https://www.youtube.com/watch?v=${vid}`;
  _prefetchProm = new Promise(res => {
    chrome.runtime.sendMessage({ action: 'analyze_youtube', url }, data => {
      _prefetchProm = null;
      if (data?.success) _formatsCache[vid] = data;
      res(data);
    });
  });
}

function showToast(parent, msg, err = false) {
  parent.querySelectorAll('.pd-yt-toast').forEach(t => t.remove());
  const t = document.createElement('div');
  t.className = 'pd-yt-toast' + (err ? ' err' : '');
  t.textContent = msg;
  parent.appendChild(t);
  setTimeout(() => t.remove(), 2800);
}

// ── Render dropdown ───────────────────────────────────────────────────────────
function renderDropdown(dd, data) {
  dd.innerHTML = '';
  let filter = 'all', query = '';

  const search = document.createElement('input');
  search.className = 'pd-yt-search';
  search.placeholder = 'Tìm: 1080p, mp4, audio...';
  search.addEventListener('input', e => { query = e.target.value.toLowerCase(); draw(); });
  dd.appendChild(search);

  const filterBar = document.createElement('div');
  filterBar.className = 'pd-yt-filters';
  filterBar.innerHTML = `
    <button class="pd-yt-filter-btn active" data-f="all">Tất cả</button>
    <button class="pd-yt-filter-btn" data-f="muxed">Video + Audio</button>
    <button class="pd-yt-filter-btn" data-f="video">Chỉ Video</button>
    <button class="pd-yt-filter-btn" data-f="audio">Chỉ Audio</button>`;
  filterBar.querySelectorAll('.pd-yt-filter-btn').forEach(b => {
    b.addEventListener('click', e => {
      e.stopPropagation();
      filterBar.querySelectorAll('.pd-yt-filter-btn').forEach(x => x.classList.remove('active'));
      b.classList.add('active');
      filter = b.dataset.f;
      draw();
    });
  });
  dd.appendChild(filterBar);

  const list = document.createElement('div');
  list.className = 'pd-yt-list';
  dd.appendChild(list);

  function draw() {
    list.innerHTML = '';
    const items = (data.formats || []).filter(f => {
      if (filter === 'muxed' && f.note === 'Audio Only') return false;
      if (filter === 'video' && f.note !== 'Video Only') return false;
      if (filter === 'audio' && f.note !== 'Audio Only') return false;
      if (query) {
        const q = query;
        const lbl = (f.height ? `${f.height}p` : 'audio') + ' ' +
          (f.ext || '') + ' ' + (f.note || '') + ' ' + (f.size || '');
        if (!lbl.toLowerCase().includes(q)) return false;
      }
      return true;
    });

    if (!items.length) {
      list.innerHTML = '<div style="color:#8b949e;font-size:13px;padding:20px;text-align:center">Không có định dạng phù hợp</div>';
      return;
    }

    items.forEach(f => {
      const item = document.createElement('div');
      item.className = 'pd-yt-item';
      const quality = f.height ? `${f.height}p` : 'Audio';
      const ext     = (f.ext || 'mp4').toUpperCase();
      let   note    = f.note ? ` · ${f.note}` : '';
      if (f.note === 'Video Only' && filter !== 'video') note = ' · Video + Audio';

      const lbl  = document.createElement('span');
      lbl.textContent = `${quality} ${ext}${note}`;
      const size = document.createElement('span');
      size.className  = 'pd-yt-size';
      size.textContent = f.size || '–';
      item.append(lbl, size);

      item.addEventListener('click', async e => {
        e.stopPropagation();
        dd.classList.remove('open');

        let fmtId = f.id;
        if (f.note === 'Video Only' && filter !== 'video') fmtId += '+bestaudio';

        const resp = await chrome.runtime.sendMessage({
          action:   'download_youtube',
          url:      location.href,
          formatId: fmtId,
          filename: `${data.title || 'video'}_${quality}.${f.ext || 'mp4'}`,
          title:    data.title,
          filesize: f.filesize || 0
        });

        showToast(dd.parentElement,
          resp?.success ? '✓ Đã thêm vào hàng chờ' : (resp?.error || '✗ Lỗi tải xuống'),
          !resp?.success);
      });
      list.appendChild(item);
    });
  }
  draw();
}

// ── Inject panel ──────────────────────────────────────────────────────────────
function injectPanel() {
  const vid = getVideoId();
  if (!vid) { removePanel(); return; }

  const isS = isShorts();

  if (vid !== _currentVid) {
    _currentVid = vid;
    _prefetchProm = null;
    removePanel();
    prefetchFormats(vid);
  } else if (document.querySelector('.pd-yt-panel')) {
    // Cùng video, panel đã có sẵn — không cần query lại player.
    return;
  }

  const player =
    document.querySelector('#movie_player') ||
    document.querySelector('.html5-video-player') ||
    (isS ? document.querySelector('ytd-reel-video-renderer[is-active] #player') : null) ||
    (isS ? document.querySelector('#shorts-player') : null);

  if (!player || player.querySelector('.pd-yt-panel')) return;

  const panel = document.createElement('div');
  panel.className = 'pd-yt-panel' + (isS ? ' shorts' : '');

  panel.innerHTML = `
    <button class="pd-yt-main-btn" id="pd-dl-btn">
      <span class="pd-yt-icon"></span>
      <span>Tải video này</span>
    </button>
    <div class="pd-yt-sep"></div>
    <button class="pd-yt-ctrl-btn" id="pd-close-btn" title="Đóng">✕</button>
  `;

  const dd = document.createElement('div');
  dd.className = 'pd-yt-dropdown';
  panel.appendChild(dd);

  const mainBtn  = panel.querySelector('#pd-dl-btn');
  const closeBtn = panel.querySelector('#pd-close-btn');

  closeBtn.addEventListener('click', e => { e.stopPropagation(); panel.style.display = 'none'; });

  mainBtn.addEventListener('click', async e => {
    e.stopPropagation();
    const closeOnOutside = ev => { if (!dd.contains(ev.target)) { dd.classList.remove('open'); document.removeEventListener('click', closeOnOutside); } };

    if (dd.classList.contains('open')) {
      dd.classList.remove('open');
      document.removeEventListener('click', closeOnOutside);
      return;
    }

    const cached = _formatsCache[_currentVid];
    if (cached) {
      renderDropdown(dd, cached);
      dd.classList.add('open');
      document.addEventListener('click', closeOnOutside);
      return;
    }

    // Waiting for analysis
    const origHtml = mainBtn.innerHTML;
    mainBtn.innerHTML = '<div class="pd-yt-spinner"></div> <span>Đang phân tích...</span>';
    mainBtn.disabled = true;

    const analyzeUrl = isS
      ? `https://www.youtube.com/shorts/${_currentVid}`
      : location.href;

    const resp = await (_prefetchProm || new Promise(res =>
      chrome.runtime.sendMessage({ action: 'analyze_youtube', url: analyzeUrl }, res)
    ));

    mainBtn.innerHTML = origHtml;
    mainBtn.disabled = false;

    if (resp?.success && resp.formats) {
      _formatsCache[_currentVid] = resp;
      renderDropdown(dd, resp);
      dd.classList.add('open');
      document.addEventListener('click', closeOnOutside);
    } else {
      showToast(panel, resp?.error || '✗ Không thể phân tích video', true);
    }
  });

  player.appendChild(panel);
}

function removePanel() {
  document.querySelectorAll('.pd-yt-panel').forEach(p => p.remove());
}

// Note: analyze_youtube & download_youtube are forwarded to background.js
// which will call PDownloader.Core HTTP bridge (localhost:6287)
// Background.js already handles these message actions — 
// here we just send messages and it handles the HTTP calls.

// ── Trigger re-inject on YouTube SPA navigation & player DOM changes ─────────
// YouTube là single-page app: URL đổi không reload trang, và player có thể
// được re-render. Thay vì poll mỗi 1.5s vô thời hạn (tốn CPU liên tục kể cả
// khi không có gì thay đổi), ta lắng nghe đúng các sự kiện liên quan:
//  - yt-navigate-finish: YouTube tự fire khi điều hướng SPA hoàn tất.
//  - MutationObserver trên #content (vùng chứa chính) để bắt trường hợp
//    player bị YouTube re-render mà không đổi URL (hiếm nhưng có thể xảy ra).
let _injectScheduled = false;
function scheduleInject() {
  if (_injectScheduled) return;
  _injectScheduled = true;
  // requestAnimationFrame: gộp nhiều mutation liên tiếp thành một lần inject,
  // tránh chạy injectPanel nhiều lần dồn dập khi YouTube re-render hàng loạt node.
  requestAnimationFrame(() => {
    _injectScheduled = false;
    injectPanel();
  });
}

document.addEventListener('yt-navigate-finish', scheduleInject);

const _ytObserver = new MutationObserver(scheduleInject);
_ytObserver.observe(document.documentElement, { childList: true, subtree: true });

// Fallback: nếu vì lý do nào đó không init kịp lúc trang load xong, vẫn thử lại
// sau một khoảng ngắn — nhưng không lặp lại vô thời hạn như setInterval cũ.
setTimeout(injectPanel, 500);
injectPanel();
