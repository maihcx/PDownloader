// ============================================================
// PDownloader Extension — Background Service Worker
// Port: localhost:6287  (PDownloader.Core HttpBridgeService)
// ============================================================

const APP_URL     = 'http://localhost:6287';
const PING_URL    = `${APP_URL}/ping`;
const DOWNLOAD_URL= `${APP_URL}/download`;
const CACHE_TTL   = 30000; // 30s

const DEFAULT_EXTENSIONS = [
  // Archives
  'zip','rar','7z','tar','gz','bz2','xz','iso','cab','lzh','gzip','z',
  // Installers
  'exe','msi','msu','apk','dmg','pkg','deb','rpm','appimage',
  // Video
  'mp4','mkv','avi','mov','wmv','webm','flv','ts','m4v','3gp','mpeg','mpg','ogv','rm','rmvb',
  // Audio
  'mp3','wav','flac','ogg','m4a','aac','wma','opus',
  // Documents
  'pdf','epub','doc','docx','xls','xlsx','ppt','pptx',
  // Other
  'torrent','img','bin','dat','iso'
];

// === STATE ===
let interceptCount = 0;
const cdCache = new Map(); // url → { filename, timestamp }

// ============================================================
// INIT
// ============================================================
chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.set({
    autoIntercept:      true,
    extensions:         DEFAULT_EXTENSIONS,
    showNotifications:  true,
    blacklistedDomains: [],
    minInterceptSizeMb: 2
  });
  createContextMenus();
});

chrome.runtime.onStartup.addListener(createContextMenus);

// ============================================================
// CONTEXT MENUS
// ============================================================
function createContextMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: 'pd-link',      title: 'Tải link này với PDownloader',   contexts: ['link'] });
    chrome.contextMenus.create({ id: 'pd-image',     title: 'Tải ảnh này với PDownloader',    contexts: ['image'] });
    chrome.contextMenus.create({ id: 'pd-media',     title: 'Tải media này với PDownloader',  contexts: ['video','audio'] });
    chrome.contextMenus.create({ id: 'pd-separator', type: 'separator',                        contexts: ['link','image','video','audio','page'] });
    chrome.contextMenus.create({ id: 'pd-page',      title: 'Tải trang này với PDownloader',  contexts: ['page'] });
  });
}

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  let url = '';
  if      (info.menuItemId === 'pd-link')  url = info.linkUrl  || '';
  else if (info.menuItemId === 'pd-image') url = info.srcUrl   || '';
  else if (info.menuItemId === 'pd-media') url = info.srcUrl   || info.linkUrl || '';
  else if (info.menuItemId === 'pd-page')  url = info.pageUrl  || tab?.url || '';
  if (!url) return;

  const filename = getFilenameFromUrl(url);
  const referer  = tab?.url || '';
  const ok = await sendToPDownloader(url, filename, referer);
  if (ok) { interceptCount++; updateBadge(); await notify(filename || url); }
});

// ============================================================
// CONTENT-DISPOSITION CACHE (webRequest)
// ============================================================
chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    if (!details.responseHeaders) return;
    for (const h of details.responseHeaders) {
      if (h.name.toLowerCase() === 'content-disposition') {
        const fn = parseContentDisposition(h.value || '');
        if (fn) {
          cdCache.set(details.url, { filename: fn, timestamp: Date.now() });
          pruneCache();
        }
      }
    }
  },
  { urls: ['<all_urls>'] },
  ['responseHeaders']
);

function parseContentDisposition(val) {
  let m = val.match(/filename\*\s*=\s*(?:UTF-8''|utf-8'')([^;\s]+)/i);
  if (m) return decodeURIComponent(m[1]);
  m = val.match(/filename\s*=\s*"([^"]+)"/i);
  if (m) return m[1];
  m = val.match(/filename\s*=\s*([^;\s]+)/i);
  if (m) return m[1].replace(/^['"]|['"]$/g, '');
  return '';
}

function pruneCache() {
  const now = Date.now();
  for (const [u, d] of cdCache) {
    if (now - d.timestamp > CACHE_TTL) cdCache.delete(u);
  }
}

// ============================================================
// DOWNLOAD INTERCEPT (chrome.downloads)
// ============================================================
chrome.downloads.onCreated.addListener(async (item) => {
  const settings = await chrome.storage.local.get(['autoIntercept','extensions','minInterceptSizeMb','blacklistedDomains']);
  if (!settings.autoIntercept) return;

  const url = item.url || '';
  if (!url || url.startsWith('blob:') || url.startsWith('data:') || url.startsWith('chrome-extension:')) return;
  if (await isBlacklisted(url, settings.blacklistedDomains || [])) return;

  // Resolve filename (Content-Disposition wins)
  let filename = item.filename || '';
  const cached = cdCache.get(url);
  if (cached && Date.now() - cached.timestamp < CACHE_TTL) {
    filename = cached.filename;
    cdCache.delete(url);
  }

  const ext  = extractExt(url, filename);
  const exts = settings.extensions || DEFAULT_EXTENSIONS;
  const minBytes = ((settings.minInterceptSizeMb ?? 2)) * 1024 * 1024;

  const byExt  = ext && exts.some(p => matchExt(p, ext));
  const bySize = minBytes > 0 && item.fileSize > 0 && item.fileSize >= minBytes;
  const byMime = matchMime(item.mime || '');

  if (!byExt && !bySize && !byMime) return;

  // Cancel browser download
  chrome.downloads.cancel(item.id);
  chrome.downloads.erase({ id: item.id });

  let referer = '';
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    referer = tab?.url || '';
  } catch (_) {}

  const displayName = filename ? filename.split(/[/\\]/).pop() : null;
  const ok = await sendToPDownloader(url, displayName, referer);
  if (ok) { interceptCount++; updateBadge(); await notify(displayName || getFilenameFromUrl(url)); }
});

// ============================================================
// SEND TO PDOWNLOADER
// ============================================================
async function sendToPDownloader(url, filename, referer) {
  let cookies = '';
  try {
    const all = await chrome.cookies.getAll({ url });
    cookies = all.map(c => `${c.name}=${c.value}`).join('; ');
  } catch (_) {}

  const payload = {
    url,
    fileName: filename || null,
    saveTo:   '',
    headers: {
      Cookie:     cookies,
      Referer:    referer || '',
      'User-Agent': navigator.userAgent
    }
  };

  try {
    const res = await fetch(DOWNLOAD_URL, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify(payload)
    });
    return res.ok;
  } catch (_) {
    return false;
  }
}

// ============================================================
// PING — check app status
// ============================================================
async function pingApp() {
  try {
    const res = await fetch(PING_URL, { signal: AbortSignal.timeout(2000) });
    return res.ok;
  } catch (_) { return false; }
}

// ============================================================
// BADGE
// ============================================================
function updateBadge() {
  if (interceptCount > 0) {
    chrome.action.setBadgeText({ text: String(interceptCount) });
    chrome.action.setBadgeBackgroundColor({ color: '#4FC3F7' });
  } else {
    chrome.action.setBadgeText({ text: '' });
  }
}

// ============================================================
// NOTIFICATIONS
// ============================================================
async function notify(label) {
  const s = await chrome.storage.local.get(['showNotifications']);
  if (s.showNotifications === false) return;

  const display = label && label.length > 55 ? label.slice(0, 52) + '…' : label || 'Download';
  const id = `pd-${Date.now()}`;

  chrome.notifications.create(id, {
    type:    'basic',
    iconUrl: 'icons/icon128.png',
    title:   'PDownloader — Đã bắt link',
    message: display,
    priority: 1
  });

  setTimeout(() => chrome.notifications.clear(id), 4000);
}

// ============================================================
// BLACKLIST HELPERS
// ============================================================
async function isBlacklisted(url, list) {
  try {
    const d = getDomain(url);
    return list.some(b => d === b || d.endsWith('.' + b));
  } catch (_) { return false; }
}

// ============================================================
// MESSAGE HANDLER (popup + content scripts)
// ============================================================
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  switch (msg.action) {
    case 'ping_app':
      pingApp().then(ok => sendResponse({ connected: ok }));
      return true;

    case 'download':
      sendToPDownloader(msg.url, msg.filename || null, msg.referer || '')
        .then(ok => {
          if (ok) { interceptCount++; updateBadge(); notify(msg.filename || msg.url); }
          sendResponse({ success: ok });
        });
      return true;

    case 'download_magnet':
      sendToPDownloader(msg.url, null, '').then(() => {});
      return false;

    case 'get_intercept_count':
      sendResponse({ count: interceptCount });
      return false;

    case 'reset_badge':
      interceptCount = 0;
      updateBadge();
      sendResponse({ success: true });
      return false;

    case 'get_settings':
      chrome.storage.local.get(
        ['autoIntercept','extensions','showNotifications','blacklistedDomains','minInterceptSizeMb'],
        data => sendResponse(data)
      );
      return true;

    case 'save_settings':
      chrome.storage.local.set(msg.settings, () => sendResponse({ success: true }));
      return true;

    case 'add_blacklist':
      chrome.storage.local.get(['blacklistedDomains'], d => {
        const list = d.blacklistedDomains || [];
        if (!list.includes(msg.domain)) list.push(msg.domain);
        chrome.storage.local.set({ blacklistedDomains: list }, () => sendResponse({ success: true }));
      });
      return true;

    case 'remove_blacklist':
      chrome.storage.local.get(['blacklistedDomains'], d => {
        const list = (d.blacklistedDomains || []).filter(x => x !== msg.domain);
        chrome.storage.local.set({ blacklistedDomains: list }, () => sendResponse({ success: true }));
      });
      return true;
  }
});

// ============================================================
// UTILITIES
// ============================================================
function getDomain(url) {
  try { return new URL(url).hostname; } catch (_) { return ''; }
}

function getFilenameFromUrl(url) {
  try {
    const p = new URL(url).pathname;
    const s = p.substring(p.lastIndexOf('/') + 1);
    return s.includes('.') ? decodeURIComponent(s) : '';
  } catch (_) { return ''; }
}

function extractExt(url, filename) {
  if (filename) {
    const clean = filename.split(/[?#]/)[0];
    const parts = clean.split('.');
    if (parts.length > 1) { const e = parts.at(-1).toLowerCase().trim(); if (e.length <= 10) return e; }
  }
  try {
    const p = decodeURIComponent(new URL(url).pathname);
    const s = p.substring(p.lastIndexOf('/') + 1);
    const parts = s.split('.');
    if (parts.length > 1) { const e = parts.at(-1).toLowerCase().trim(); if (e.length <= 10) return e; }
  } catch (_) {}
  return '';
}

function matchExt(pattern, ext) {
  const p = pattern.trim().toLowerCase();
  if (p.includes('*')) return new RegExp('^' + p.replace(/\*/g, '.*') + '$').test(ext);
  return p === ext;
}

function matchMime(mime) {
  const m = mime.toLowerCase();
  return ['application/octet-stream','application/zip','application/x-rar',
          'application/x-7z','application/pdf','application/x-bittorrent',
          'video/','audio/'].some(t => m.startsWith(t));
}

// ============================================================
// YOUTUBE — analyze & download (forwarded to PDownloader.Core)
// ============================================================
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.action === 'analyze_youtube') {
    fetch(`${APP_URL}/youtube/analyze`, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ url: msg.url })
    })
    .then(r => r.ok ? r.json() : { success: false, error: `Server ${r.status}` })
    .catch(() => ({ success: false, error: 'Không thể kết nối đến PDownloader.' }))
    .then(sendResponse);
    return true;
  }

  if (msg.action === 'download_youtube') {
    fetch(`${APP_URL}/youtube/download`, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({
        url:      msg.url,
        formatId: msg.formatId,
        filename: msg.filename,
        title:    msg.title,
        filesize: msg.filesize || 0
      })
    })
    .then(r => r.ok ? r.json() : { success: false, error: `Server ${r.status}` })
    .catch(() => ({ success: false, error: 'Không thể kết nối đến PDownloader.' }))
    .then(sendResponse);
    return true;
  }
});
