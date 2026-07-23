const _yt_themeLink = document.createElement('link');
_yt_themeLink.rel  = 'stylesheet';
_yt_themeLink.href = PDWebExt.runtime.getURL('common/theme.css');
document.head.appendChild(_yt_themeLink);

const _yt_style = document.createElement('style');
_yt_style.textContent = `
.pd-yt-panel {
  position: absolute;
  top: 12px; right: 12px;
  z-index: 99999;
  font-family: 'Segoe UI', system-ui, sans-serif;
  user-select: none;
  background: var(--pd-bg);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border: 1px solid var(--pd-border);
  border-radius: 10px;
  box-shadow: 0 6px 28px var(--pd-shadow),
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
  color: var(--pd-text); font-size: 13px; font-weight: 600;
  font-family: inherit; padding: 0 10px;
  cursor: pointer; height: 100%;
  border-radius: 7px; transition: background .18s, color .15s;
  box-sizing: border-box;
}
.pd-yt-main-btn:hover  { background: var(--pd-accent-bg); color: var(--pd-text); }
.pd-yt-main-btn:active { transform: scale(0.97); }

.pd-yt-icon {
  width: 0; height: 0;
  border-left: 10px solid var(--pd-accent);
  border-top: 6px solid transparent;
  border-bottom: 6px solid transparent;
  display: inline-block;
  transition: border-left-color .15s;
}

.pd-yt-sep {
  width: 1px; height: 18px;
  background: var(--pd-border2); margin: 0 2px;
}

.pd-yt-ctrl-btn {
  background: transparent; border: none;
  color: var(--pd-muted); font-size: 13px; padding: 0 8px;
  cursor: pointer; display: flex; align-items: center;
  justify-content: center; height: 100%;
  border-radius: 7px; transition: background .18s, color .15s;
  font-weight: 600; box-sizing: border-box;
}
.pd-yt-ctrl-btn:hover { background: var(--pd-border2); color: var(--pd-text); }

/* Dropdown */
.pd-yt-dropdown {
  position: absolute;
  top: calc(100% + 8px); right: 0;
  width: 460px; max-height: 380px;
  background: var(--pd-dropdown);
  backdrop-filter: blur(20px);
  border: 1px solid var(--pd-border);
  border-radius: 12px;
  box-shadow: 0 16px 48px var(--pd-shadow);
  padding: 10px; display: none;
  flex-direction: column; gap: 6px;
  z-index: 100000; overflow: hidden;
}
.pd-yt-dropdown.open { display: flex; }
.pd-yt-panel.shorts .pd-yt-dropdown { right: auto; left: 0; }

.pd-yt-search {
  width: 100%; background: rgba(255,255,255,0.05);
  border: 1px solid var(--pd-border);
  color: var(--pd-text); padding: 7px 12px;
  border-radius: 8px; font-size: 13px;
  font-family: inherit; outline: none;
  transition: border-color .2s;
  box-sizing: border-box;
}
.pd-yt-search:focus { border-color: var(--pd-accent); background: var(--pd-accent-bg); }
.pd-yt-search::placeholder { color: var(--pd-muted); }

.pd-yt-filters {
  display: flex; gap: 6px;
  padding-bottom: 8px;
  border-bottom: 1px solid rgba(255,255,255,0.08);
  flex-shrink: 0;
}
.pd-yt-filter-btn {
  background: rgba(255,255,255,0.04);
  border: 1px solid rgba(255,255,255,0.1);
  color: var(--pd-muted); padding: 5px 12px;
  border-radius: 6px; font-size: 12px; font-weight: 600;
  cursor: pointer; font-family: inherit; transition: all .18s;
}
.pd-yt-filter-btn:hover { border-color: var(--pd-accent); color: var(--pd-text); }
.pd-yt-filter-btn.active {
  background: var(--pd-accent-bg);
  border-color: var(--pd-accent); color: var(--pd-text);
}

.pd-yt-list { display: flex; flex-direction: column; gap: 2px; overflow-y: auto; flex: 1; }

.pd-yt-empty { color: var(--pd-muted); font-size: 13px; padding: 20px; text-align: center; }

.pd-yt-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 8px 14px; color: var(--pd-text); font-size: 13px;
  cursor: pointer; border-radius: 8px; transition: background .15s;
}
.pd-yt-item:hover { background: var(--pd-accent-bg); color: var(--pd-text); }
.pd-yt-item .pd-yt-size {
  color: var(--pd-accent); font-size: 12px; font-weight: 700; margin-left: 12px; flex-shrink: 0;
}

.pd-yt-spinner {
  width: 14px; height: 14px;
  border: 2px solid rgba(255,255,255,0.15);
  border-top-color: var(--pd-accent);
  border-radius: 50%;
  animation: pd-spin .6s linear infinite; display: inline-block;
}
@keyframes pd-spin { to { transform: rotate(360deg); } }

.pd-yt-toast {
  position: absolute; top: calc(100% + 8px); right: 0;
  background: var(--pd-green-bg); color: var(--pd-green);
  border: 1px solid var(--pd-green);
  font-size: 13px; padding: 7px 16px;
  border-radius: 8px; white-space: nowrap;
  box-shadow: 0 6px 20px var(--pd-shadow);
  animation: pd-toast-in 2.8s forwards; pointer-events: none; z-index: 100001;
}
.pd-yt-toast.err { background: var(--pd-red-bg); color: #fff; border-color: var(--pd-red); }
@keyframes pd-toast-in {
  0%   { opacity:0; transform: translateY(-4px); }
  10%  { opacity:1; transform: translateY(0); }
  88%  { opacity:1; }
  100% { opacity:0; }
}
`;
document.head.appendChild(_yt_style);

let _formatsCache = {};
let _currentVid   = '';
let _prefetchProm = null;

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
    PDWebExt.runtime.sendMessage({ action: 'analyze_youtube', url }, data => {
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

function renderDropdown(dd, data) {
  dd.replaceChildren();
  let filter = 'all', query = '';

  const search = document.createElement('input');
  search.className = 'pd-yt-search';
  search.placeholder = PD.I18n.t('ytSearchPlaceholder');
  search.addEventListener('input', e => { query = e.target.value.toLowerCase(); draw(); });

  ['keydown', 'keyup', 'keypress'].forEach(evt => {
    search.addEventListener(evt, e => e.stopPropagation());
  });

  dd.appendChild(search);

  const filterBar = document.createElement('div');
  filterBar.className = 'pd-yt-filters';

  const filterButtons = [
    ['all', 'ytFilterAll'],
    ['muxed', 'ytFilterMuxed'],
    ['video', 'ytFilterVideo'],
    ['audio', 'ytFilterAudio']
  ].map(([value, labelKey], index) => {
    const button = document.createElement('button');
    button.className = 'pd-yt-filter-btn' + (index === 0 ? ' active' : '');
    button.dataset.f = value;
    button.textContent = PD.I18n.t(labelKey);
    filterBar.appendChild(button);
    return button;
  });

  filterButtons.forEach(b => {
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
    list.replaceChildren();
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
      const empty = document.createElement('div');
      empty.className = 'pd-yt-empty';
      empty.textContent = PD.I18n.t('ytNoFormats');
      list.appendChild(empty);
      return;
    }

    items.forEach(f => {
      const item = document.createElement('div');
      item.className = 'pd-yt-item';
      const quality = f.height ? `${f.height}p` : 'Audio';
      const ext     = (f.ext || 'mp4').toUpperCase();
      let   note    = f.note ? ` · ${f.note}` : '';
      if (f.note === 'Video Only' && filter !== 'video') note = ' · ' + PD.I18n.t('ytFilterMuxed');

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

        const resp = await PDWebExt.runtime.sendMessage({
          action:   'download_youtube',
          url:      location.href,
          formatId: fmtId,
          filename: `${data.title || 'video'}_${quality}.${f.ext || 'mp4'}`,
          title:    data.title,
          filesize: f.filesize || 0
        });

        showToast(dd.parentElement,
          resp?.success ? PD.I18n.t('ytAddedToQueue') : (resp?.error || PD.I18n.t('ytDownloadError')),
          !resp?.success);
      });
      list.appendChild(item);
    });
  }
  draw();
}

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
    return;
  }

  const player =
    document.querySelector('#movie_player') ||
    document.querySelector('.html5-video-player') ||
    (isS ? document.querySelector('ytd-reel-video-renderer[is-active] #player') : null) ||
    (isS ? document.querySelector('#shorts-player') : null);

  if (!player || player.querySelector('.pd-yt-panel')) return;

  const panel = document.createElement('div');
  panel.className = 'pd-yt-panel pd-theme-root' + (isS ? ' shorts' : '');

  const mainBtn = document.createElement('button');
  mainBtn.className = 'pd-yt-main-btn';
  mainBtn.id = 'pd-dl-btn';

  const setMainButtonContent = loading => {
    const icon = document.createElement(loading ? 'div' : 'span');
    icon.className = loading ? 'pd-yt-spinner' : 'pd-yt-icon';

    const label = document.createElement('span');
    label.textContent = PD.I18n.t(loading ? 'ytAnalyzing' : 'ytDownloadThisVideo');

    mainBtn.replaceChildren(icon, label);
  };
  setMainButtonContent(false);

  const separator = document.createElement('div');
  separator.className = 'pd-yt-sep';

  const closeBtn = document.createElement('button');
  closeBtn.className = 'pd-yt-ctrl-btn';
  closeBtn.id = 'pd-close-btn';
  closeBtn.title = PD.I18n.t('ytClose');
  closeBtn.textContent = '✕';

  const dd = document.createElement('div');
  dd.className = 'pd-yt-dropdown';

  panel.append(mainBtn, separator, closeBtn, dd);

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
    setMainButtonContent(true);
    mainBtn.disabled = true;

    const analyzeUrl = isS
      ? `https://www.youtube.com/shorts/${_currentVid}`
      : location.href;

    const resp = await (_prefetchProm || new Promise(res =>
      PDWebExt.runtime.sendMessage({ action: 'analyze_youtube', url: analyzeUrl }, res)
    ));

    setMainButtonContent(false);
    mainBtn.disabled = false;

    if (resp?.success && resp.formats) {
      _formatsCache[_currentVid] = resp;
      renderDropdown(dd, resp);
      dd.classList.add('open');
      document.addEventListener('click', closeOnOutside);
    } else {
      showToast(panel, resp?.error || PD.I18n.t('ytCannotAnalyze'), true);
    }
  });

  player.appendChild(panel);
}

function removePanel() {
  document.querySelectorAll('.pd-yt-panel').forEach(p => p.remove());
}

let _injectScheduled = false;
function scheduleInject() {
  if (_injectScheduled) return;
  _injectScheduled = true;

  requestAnimationFrame(() => {
    _injectScheduled = false;
    injectPanel();
  });
}

document.addEventListener('yt-navigate-finish', scheduleInject);

const _ytObserver = new MutationObserver(scheduleInject);
_ytObserver.observe(document.documentElement, { childList: true, subtree: true });

setTimeout(injectPanel, 500);
injectPanel();
