(() => {
  if (window.__pdInstagramBridgeInstalled) return;
  window.__pdInstagramBridgeInstalled = true;

  const RESOLVE_EVENT = 'pd-instagram-resolve';
  const URL_ATTRIBUTE = 'data-pd-instagram-url';
  const TITLE_ATTRIBUTE = 'data-pd-instagram-title';
  const mediaByCode = new Map();
  const mediaOrder = [];

  function cleanText(value, maxLength = 180) {
    return String(value || '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, maxLength) || null;
  }

  function validCode(value) {
    const code = String(value || '').trim();
    return /^[A-Za-z0-9_-]{5,32}$/.test(code) ? code : null;
  }

  function getUsername(value) {
    const username = String(value || '').trim().replace(/^@/, '');
    return /^[A-Za-z0-9._]{1,64}$/.test(username) ? username.toLowerCase() : null;
  }

  function firstValue(...values) {
    return values.find(value => value !== undefined && value !== null && value !== '') ?? null;
  }

  function getMediaUrlKey(value) {
    if (!value) return null;

    try {
      const url = new URL(String(value), location.href);
      if (!/^https?:$/.test(url.protocol)) return null;
      return `${url.hostname.toLowerCase()}${url.pathname}`;
    } catch (_) {
      return null;
    }
  }

  function collectVideoUrlKeys(object) {
    const values = [
      object.video_url,
      object.videoUrl,
      object.playback_url,
      object.playbackUrl
    ];

    for (const version of object.video_versions || object.videoVersions || []) {
      values.push(version?.url, version?.src);
    }

    return [...new Set(values.map(getMediaUrlKey).filter(Boolean))];
  }

  function normalizeMedia(object) {
    if (!object || typeof object !== 'object') return null;

    const code = validCode(firstValue(object.code, object.shortcode));
    if (!code) return null;

    const typeName = String(object.__typename || '');
    const hasMediaShape = /Media|ClipsItem/i.test(typeName)
      || object.media_type != null
      || object.product_type === 'clips'
      || object.video_versions != null
      || object.clips_metadata != null
      || object.pk != null
      || (object.id != null && (object.user != null || object.owner != null));

    if (!hasMediaShape) return null;

    const user = object.user || object.owner || object.media_owner || null;
    const clips = object.clips_metadata || object.clipsMetadata || null;
    const musicInfo = clips?.music_info || clips?.musicInfo || null;
    const musicAsset = musicInfo?.music_asset_info || musicInfo?.musicAssetInfo || null;
    const originalSound = clips?.original_sound_info || clips?.originalSoundInfo || null;
    const caption = object.caption || object.edge_media_to_caption?.edges?.[0]?.node || null;

    const username = getUsername(firstValue(
      user?.username,
      object.username,
      object.owner_username,
      object.author?.username
    ));

    const audioId = String(firstValue(
      musicAsset?.audio_cluster_id,
      musicAsset?.audioClusterId,
      originalSound?.audio_asset_id,
      originalSound?.audioAssetId,
      originalSound?.audio_id,
      originalSound?.audioId,
      object.audio_cluster_id
    ) || '').match(/\d{5,}/)?.[0] || null;

    const title = cleanText(firstValue(
      caption?.text,
      object.caption_text,
      object.title,
      object.accessibility_caption
    ));

    return {
      code,
      url: `https://www.instagram.com/reel/${code}/`,
      username,
      audioId,
      title,
      videoUrlKeys: collectVideoUrlKeys(object),
      id: String(firstValue(object.pk, object.id) || '') || null
    };
  }

  function registerMedia(media) {
    if (!media?.code) return;

    const existing = mediaByCode.get(media.code);
    if (existing) {
      Object.assign(existing, {
        username: media.username || existing.username,
        audioId: media.audioId || existing.audioId,
        title: media.title || existing.title,
        videoUrlKeys: [...new Set([
          ...(existing.videoUrlKeys || []),
          ...(media.videoUrlKeys || [])
        ])],
        id: media.id || existing.id
      });
      return;
    }

    mediaByCode.set(media.code, media);
    mediaOrder.push(media);
  }

  function collectMedia(value, maxNodes = 120000) {
    if (!value || typeof value !== 'object') return;

    const stack = [value];
    const seen = new WeakSet();
    let visited = 0;

    while (stack.length && visited < maxNodes) {
      const current = stack.pop();
      if (!current || typeof current !== 'object') continue;
      if (seen.has(current)) continue;
      seen.add(current);
      visited++;

      const media = normalizeMedia(current);
      if (media) registerMedia(media);

      if (Array.isArray(current)) {
        for (let i = current.length - 1; i >= 0; i--) {
          const item = current[i];
          if (item && typeof item === 'object') stack.push(item);
        }
        continue;
      }

      for (const key of Object.keys(current)) {
        let child;
        try { child = current[key]; } catch (_) { continue; }
        if (child && typeof child === 'object') stack.push(child);
      }
    }
  }

  function parseJsonText(text) {
    let value = String(text || '').trim();
    if (!value || value.length > 8_000_000) return;
    if (!value.includes('"code"') && !value.includes('"shortcode"')) return;

    value = value.replace(/^for\s*\(;;\);?\s*/, '');

    try {
      collectMedia(JSON.parse(value));
      return;
    } catch (_) { }

    for (const line of value.split(/\r?\n/)) {
      const candidate = line.trim().replace(/^for\s*\(;;\);?\s*/, '');
      if (!candidate || (!candidate.startsWith('{') && !candidate.startsWith('['))) continue;
      try { collectMedia(JSON.parse(candidate)); } catch (_) { }
    }
  }

  function parseScript(script) {
    if (!(script instanceof HTMLScriptElement)) return;
    const text = script.textContent || '';
    if (!text.includes('"code"') && !text.includes('"shortcode"')) return;
    parseJsonText(text);
  }

  function parseExistingScripts() {
    for (const script of document.querySelectorAll('script[type="application/json"], script[data-sjs]')) {
      parseScript(script);
    }
  }

  function installScriptObserver() {
    const observer = new MutationObserver(records => {
      for (const record of records) {
        for (const node of record.addedNodes) {
          if (!(node instanceof Element)) continue;
          if (node instanceof HTMLScriptElement) parseScript(node);
          for (const script of node.querySelectorAll?.('script[type="application/json"], script[data-sjs]') || []) {
            parseScript(script);
          }
        }
      }
    });

    observer.observe(document.documentElement, { childList: true, subtree: true });
  }

  function shouldInspectResponse(url) {
    const value = String(url || '');
    return /instagram\.com\/(?:api\/graphql|graphql\/query|api\/v1|ajax\/)/i.test(value)
      || /clips|reels/i.test(value);
  }

  function installFetchCapture() {
    const originalFetch = window.fetch;
    if (typeof originalFetch !== 'function') return;

    window.fetch = async function (...args) {
      const response = await originalFetch.apply(this, args);
      try {
        const requestUrl = args[0]?.url || args[0] || response.url;
        if (shouldInspectResponse(requestUrl)) {
          response.clone().text().then(parseJsonText).catch(() => { });
        }
      } catch (_) { }
      return response;
    };
  }

  function installXhrCapture() {
    const originalOpen = XMLHttpRequest.prototype.open;
    const originalSend = XMLHttpRequest.prototype.send;

    XMLHttpRequest.prototype.open = function (method, url, ...rest) {
      this.__pdInstagramUrl = String(url || '');
      return originalOpen.call(this, method, url, ...rest);
    };

    XMLHttpRequest.prototype.send = function (...args) {
      if (!this.__pdInstagramCaptureInstalled) {
        this.__pdInstagramCaptureInstalled = true;
        this.addEventListener('load', () => {
          try {
            if (!shouldInspectResponse(this.__pdInstagramUrl || this.responseURL)) return;
            if (this.responseType === 'json') {
              collectMedia(this.response);
            } else if (!this.responseType || this.responseType === 'text') {
              parseJsonText(this.responseText);
            }
          } catch (_) { }
        });
      }
      return originalSend.apply(this, args);
    };
  }

  function getReelContainer(video) {
    if (!(video instanceof HTMLVideoElement)) return null;

    let current = video;
    let best = video.parentElement;

    for (let depth = 0; depth < 24 && current?.parentElement; depth++) {
      const parent = current.parentElement;
      let count = 0;
      try { count = parent.querySelectorAll('video').length; } catch (_) { }
      if (count > 1) break;
      if (count === 1) best = parent;
      current = parent;
    }

    return best;
  }

  function getDomSignature(video) {
    const container = getReelContainer(video) || video.parentElement || video;
    let code = null;
    let username = null;
    let audioId = null;
    const videoUrlKey = getMediaUrlKey(video.currentSrc || video.src);

    for (const anchor of container.querySelectorAll?.('a[href]') || []) {
      const raw = anchor.getAttribute('href') || anchor.href || '';
      let pathname = raw;
      try { pathname = new URL(raw, location.href).pathname; } catch (_) { }

      if (!code) {
        code = validCode(pathname.match(/^\/(?:reel|p|tv)\/([A-Za-z0-9_-]+)\/?$/i)?.[1]);
      }

      if (!username) {
        const match = pathname.match(/^\/([^/]+)\/reels\/?$/i);
        if (match && match[1].toLowerCase() !== 'explore') {
          username = getUsername(match[1]);
        }
      }

      if (!audioId) {
        audioId = pathname.match(/^\/reels\/audio\/(\d+)\/?$/i)?.[1] || null;
      }

      if (code && username && audioId) break;
    }

    return { container, code, username, audioId, videoUrlKey };
  }

  function scoreMedia(media, signature) {
    let score = 0;
    if (signature.videoUrlKey && media.videoUrlKeys?.includes(signature.videoUrlKey)) score += 20000;
    if (signature.code && media.code === signature.code) score += 10000;
    if (signature.username && media.username === signature.username) score += 300;
    if (signature.audioId && media.audioId === signature.audioId) score += 450;
    if (signature.username && media.username && media.username !== signature.username) score -= 250;
    if (signature.audioId && media.audioId && media.audioId !== signature.audioId) score -= 300;
    return score;
  }

  function findCachedMedia(video) {
    const signature = getDomSignature(video);

    if (signature.code) {
      const exact = mediaByCode.get(signature.code);
      if (exact) return exact;
    }

    let best = null;
    let bestScore = 0;

    for (const media of mediaByCode.values()) {
      const score = scoreMedia(media, signature);
      if (score > bestScore) {
        best = media;
        bestScore = score;
      }
    }

    // Không ánh xạ theo thứ tự toàn cục của video/media: Instagram tái sử dụng DOM
    // khi cuộn Reel, nên index rất dễ trỏ về video đã xem trước đó.
    return best && bestScore >= 700 ? best : null;
  }

  function collectReactCandidates(node, signature) {
    const candidates = new Map();
    const visitedObjects = new WeakSet();
    let budget = 10000;

    function inspect(value, depth = 0) {
      if (!value || typeof value !== 'object' || depth > 9 || budget-- <= 0) return;
      if (visitedObjects.has(value)) return;
      visitedObjects.add(value);

      const media = normalizeMedia(value);
      if (media) {
        registerMedia(media);
        const matchScore = scoreMedia(media, signature);
        const score = (matchScore * 10) + 1000 - depth;
        const previous = candidates.get(media.code);
        if (!previous || score > previous.score) {
          candidates.set(media.code, { media, score, matchScore });
        }
      }

      if (Array.isArray(value)) {
        for (let i = 0; i < Math.min(value.length, 80); i++) inspect(value[i], depth + 1);
        return;
      }

      const preferredKeys = [
        'media', 'item', 'clipsItem', 'reel', 'post', 'node', 'data', 'edges',
        'memoizedProps', 'pendingProps', 'memoizedState', 'return', 'child', 'sibling'
      ];

      for (const key of preferredKeys) {
        let child;
        try { child = value[key]; } catch (_) { continue; }
        if (child && typeof child === 'object') inspect(child, depth + 1);
      }

      if (depth < 4) {
        const keys = Object.keys(value);
        for (let i = 0; i < Math.min(keys.length, 120); i++) {
          const key = keys[i];
          if (preferredKeys.includes(key)) continue;
          let child;
          try { child = value[key]; } catch (_) { continue; }
          if (child && typeof child === 'object') inspect(child, depth + 1);
        }
      }
    }

    let element = node;
    for (let level = 0; level < 20 && element; level++, element = element.parentElement) {
      let propertyNames = [];
      try { propertyNames = Object.getOwnPropertyNames(element); } catch (_) { }

      for (const key of propertyNames) {
        if (!/^__(?:reactFiber|reactProps|reactContainer)\$/.test(key)) continue;
        try { inspect(element[key], 0); } catch (_) { }
      }
    }

    const best = [...candidates.values()].sort((a, b) => b.score - a.score)[0];
    return best && best.matchScore >= 700 ? best.media : null;
  }

  function resolveMedia(video) {
    if (!(video instanceof HTMLVideoElement)) return null;

    try { collectMedia(history.state, 12000); } catch (_) { }

    const signature = getDomSignature(video);
    return collectReactCandidates(video, signature) || findCachedMedia(video);
  }

  function handleResolve(event) {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;

    const video = target instanceof HTMLVideoElement
      ? target
      : target.querySelector?.('video') || target.closest?.('video');

    target.removeAttribute(URL_ATTRIBUTE);
    target.removeAttribute(TITLE_ATTRIBUTE);
    if (video && video !== target) {
      video.removeAttribute(URL_ATTRIBUTE);
      video.removeAttribute(TITLE_ATTRIBUTE);
    }

    const media = resolveMedia(video);
    if (!media) return;

    const title = media.title || (media.username ? `Instagram - @${media.username}` : 'Instagram Reel');

    for (const element of [target, video]) {
      if (!(element instanceof Element)) continue;
      element.setAttribute(URL_ATTRIBUTE, media.url);
      element.setAttribute(TITLE_ATTRIBUTE, title);
    }
  }

  document.addEventListener(RESOLVE_EVENT, handleResolve, true);

  try { installFetchCapture(); } catch (_) { }
  try { installXhrCapture(); } catch (_) { }

  if (document.documentElement) installScriptObserver();
  else document.addEventListener('readystatechange', () => {
    if (document.documentElement) installScriptObserver();
  }, { once: true });

  parseExistingScripts();
  document.addEventListener('DOMContentLoaded', parseExistingScripts, { once: true });
})();
