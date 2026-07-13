(function (root) {
  const PD = root.PD || (root.PD = {});

  const SITE_CONTAINERS = {
    tiktok: [
      '[data-e2e="recommend-list-item-container"]',
      '[data-e2e="browse-video"]',
      '[data-e2e="feed-video"]',
      '[data-testid*="video"]',
      'article'
    ].join(','),
    facebook: [
      '[data-video-id]',
      '[role="article"]',
      'article',
      '[data-pagelet^="FeedUnit"]',
      '[data-pagelet*="FeedUnit"]',
      '[data-testid*="post"]'
    ].join(','),
    instagram: [
      'article',
      '[role="presentation"]'
    ].join(',')
  };

  const TRACKING_PARAMS = new Set([
    'fbclid', 'mibextid', '__cft__', '__tn__', 'ref', 'refsrc', 'refid',
    'paipv', 'eav', 'rdid', 'sfnsn', 'hoisted_section_header_type',
    '_r', 'checksum', 'is_from_webapp', 'sender_device', 'sender_web_id',
    'web_id', 'refer', 'referer_url', 'enter_from', 'source', 'lang'
  ]);

  function resolve(video, contextNode = video) {
    const site = detectSite(location.hostname);
    if (!site) return location.href;

    const current = normalizeUrl(location.href, site);

    const directVideoUrl = getDirectVideoUrl(video, contextNode, site);
    if (directVideoUrl) return directVideoUrl;

    const roots = collectSearchRoots(video, contextNode, site);
    const candidates = collectCandidates(roots, video, site);

    if (candidates.length > 0) {
      candidates.sort((a, b) => b.score - a.score);
      return candidates[0].url;
    }

    if (current && getUrlQuality(current, site) > 0) return current;

    const canonical = getCanonicalUrl(site);
    if (canonical && getUrlQuality(canonical, site) > 0) return canonical;

    return current || location.href;
  }

  function getDirectVideoUrl(video, contextNode, site) {
    if (site === 'instagram') {
      return getInstagramDomPermalink(video, contextNode)
        || requestInstagramMetadata(video, contextNode)?.url
        || null;
    }

    if (site === 'tiktok') {
      return getTikTokDirectVideoUrl(video, contextNode);
    }

    if (site !== 'facebook') return null;

    const nodes = [];
    const add = (node) => {
      if (node && !nodes.includes(node)) nodes.push(node);
    };

    add(safeClosest(video, '[data-video-id]'));
    add(safeClosest(contextNode, '[data-video-id]'));

    for (const node of nodes) {
      const id = String(node.getAttribute?.('data-video-id') || '').trim();
      if (/^\d{5,}$/.test(id)) {
        return `https://www.facebook.com/watch/?v=${encodeURIComponent(id)}`;
      }
    }

    return null;
  }

  const INSTAGRAM_RESOLVE_EVENT = 'pd-instagram-resolve';
  const INSTAGRAM_URL_ATTRIBUTE = 'data-pd-instagram-url';
  const INSTAGRAM_TITLE_ATTRIBUTE = 'data-pd-instagram-title';

  function getInstagramDomPermalink(video, contextNode) {
    const roots = [];
    const seen = new Set();

    const add = (node) => {
      if (!(node instanceof Element) || seen.has(node)) return;
      seen.add(node);
      roots.push(node);
    };

    add(safeClosest(video, 'a[href]'));
    add(safeClosest(contextNode, 'a[href]'));
    add(safeClosest(video, 'article'));
    add(safeClosest(contextNode, 'article'));
    add(safeClosest(video, '[role="presentation"]'));
    add(safeClosest(contextNode, '[role="presentation"]'));
    add(contextNode);
    add(video);

    const videoRect = safeRect(video);
    let best = null;

    for (const root of roots) {
      const anchors = [];
      if (isAnchor(root)) anchors.push(root);

      try {
        for (const anchor of root.querySelectorAll?.(
          'a[href*="/reel/"],a[href*="/p/"],a[href*="/tv/"]'
        ) || []) {
          anchors.push(anchor);
        }
      } catch (_) { }

      for (const anchor of anchors) {
        const url = normalizeUrl(anchor.href || anchor.getAttribute?.('href'), 'instagram');
        const quality = url ? getUrlQuality(url, 'instagram') : 0;
        if (quality <= 0) continue;

        let score = quality + getProximityBonus(videoRect, safeRect(anchor));
        if (anchor.contains?.(video)) score += 120;
        if (safeClosest(anchor, 'article') === safeClosest(video, 'article')) score += 80;

        if (!best || score > best.score) best = { url, score };
      }
    }

    return best?.url || null;
  }

  function requestInstagramMetadata(video, contextNode) {
    const nodes = [];
    const add = (node) => {
      if (node instanceof Element && !nodes.includes(node)) nodes.push(node);
    };

    add(video);
    add(contextNode);

    for (const node of nodes) {
      try {
        node.removeAttribute(INSTAGRAM_URL_ATTRIBUTE);
        node.removeAttribute(INSTAGRAM_TITLE_ATTRIBUTE);
        node.dispatchEvent(new Event(INSTAGRAM_RESOLVE_EVENT, {
          bubbles: true,
          composed: true
        }));

        const rawUrl = node.getAttribute(INSTAGRAM_URL_ATTRIBUTE);
        const url = normalizeUrl(rawUrl, 'instagram');
        if (!url || getUrlQuality(url, 'instagram') <= 0) continue;

        return {
          url,
          title: cleanMediaTitle(node.getAttribute(INSTAGRAM_TITLE_ATTRIBUTE))
        };
      } catch (_) { }
    }

    return null;
  }

  function getTikTokDirectVideoUrl(video, contextNode) {
    const roots = collectTikTokRoots(video, contextNode);

    for (const root of roots) {
      const permalink = findTikTokPermalink(root);
      if (permalink) return permalink;
    }

    const itemId = findTikTokItemId(roots);
    if (!itemId) return null;

    const hydrationItem = getTikTokHydrationItem(itemId);
    const username = findTikTokUsername(roots)
      || normalizeTikTokUsername(hydrationItem?.author?.uniqueId);

    if (!username) return null;

    return `https://www.tiktok.com/@${encodeURIComponent(username)}/video/${itemId}`;
  }

  function collectTikTokRoots(video, contextNode) {
    const roots = [];
    const seen = new Set();

    const add = (node) => {
      if (!node || seen.has(node)) return;
      seen.add(node);
      roots.push(node);
    };

    add(safeClosest(video, '[id^="xgwrapper-"]'));
    add(safeClosest(contextNode, '[id^="xgwrapper-"]'));
    add(safeClosest(video, '[data-e2e="feed-video"]'));
    add(safeClosest(contextNode, '[data-e2e="feed-video"]'));
    add(safeClosest(video, '[data-e2e="recommend-list-item-container"]'));
    add(safeClosest(contextNode, '[data-e2e="recommend-list-item-container"]'));
    add(safeClosest(video, 'article'));
    add(safeClosest(contextNode, 'article'));
    add(contextNode);
    add(video);

    return roots;
  }

  function findTikTokPermalink(root) {
    if (!root) return null;

    const anchors = [];
    if (isAnchor(root)) anchors.push(root);

    try {
      for (const anchor of root.querySelectorAll?.('a[href*="/video/"]') || []) {
        anchors.push(anchor);
      }
    } catch (_) { }

    for (const anchor of anchors) {
      const url = normalizeUrl(anchor.href || anchor.getAttribute?.('href'), 'tiktok');
      if (url && getUrlQuality(url, 'tiktok') > 0) return url;
    }

    return null;
  }

  function findTikTokItemId(roots) {
    for (const root of roots) {
      const direct = extractTikTokItemId(root);
      if (direct) return direct;

      let nodes = [];
      try {
        nodes = root?.querySelectorAll?.(
          '[id^="xgwrapper-"],[data-item-id],[data-video-id],[data-aweme-id]'
        ) || [];
      } catch (_) { }

      for (let i = 0; i < nodes.length && i < 100; i++) {
        const id = extractTikTokItemId(nodes[i]);
        if (id) return id;
      }

      let mediaNodes = [];
      try {
        mediaNodes = root?.querySelectorAll?.('video[src],source[src]') || [];
      } catch (_) { }

      for (let i = 0; i < mediaNodes.length && i < 20; i++) {
        const raw = mediaNodes[i].currentSrc || mediaNodes[i].src || mediaNodes[i].getAttribute?.('src');
        const id = extractTikTokItemIdFromUrl(raw);
        if (id) return id;
      }
    }

    return null;
  }

  function extractTikTokItemId(node) {
    if (!node) return null;

    const values = [
      node.id,
      node.getAttribute?.('data-item-id'),
      node.getAttribute?.('data-video-id'),
      node.getAttribute?.('data-aweme-id')
    ];

    for (const value of values) {
      const text = String(value || '');
      const wrapperMatch = text.match(/xgwrapper-.*?(\d{15,22})(?:\D|$)/i);
      if (wrapperMatch) return wrapperMatch[1];

      if (/^\d{15,22}$/.test(text)) return text;
    }

    return null;
  }

  function extractTikTokItemIdFromUrl(rawUrl) {
    if (!rawUrl || String(rawUrl).startsWith('blob:')) return null;

    try {
      const url = new URL(rawUrl, location.href);
      const queryId = url.searchParams.get('item_id') || url.searchParams.get('aweme_id');
      if (/^\d{15,22}$/.test(queryId || '')) return queryId;

      return url.pathname.match(/\/video\/(\d{15,22})(?:\/|$)/i)?.[1] || null;
    } catch (_) {
      return String(rawUrl).match(/(?:item_id|aweme_id)=([0-9]{15,22})/i)?.[1] || null;
    }
  }

  function findTikTokUsername(roots) {
    const selectors = [
      'a[data-e2e="video-author-avatar"][href]',
      '[data-e2e="video-author-uniqueid"] a[href]',
      'a[href^="/@"]',
      'a[href*="tiktok.com/@"]'
    ];

    for (const root of roots) {
      if (!root) continue;

      const candidates = [];
      if (isAnchor(root)) candidates.push(root);

      for (const selector of selectors) {
        try {
          const anchor = root.querySelector?.(selector);
          if (anchor) candidates.push(anchor);
        } catch (_) { }
      }

      for (const anchor of candidates) {
        const username = extractTikTokUsernameFromUrl(
          anchor.href || anchor.getAttribute?.('href')
        );
        if (username) return username;
      }
    }

    return null;
  }

  function extractTikTokUsernameFromUrl(rawUrl) {
    if (!rawUrl) return null;

    try {
      const url = new URL(rawUrl, location.href);
      return normalizeTikTokUsername(url.pathname.match(/^\/@([^/?#]+)/i)?.[1]);
    } catch (_) {
      return normalizeTikTokUsername(String(rawUrl).match(/(?:^|\/)@([^/?#]+)/)?.[1]);
    }
  }

  function normalizeTikTokUsername(value) {
    if (!value) return null;

    let username = String(value).trim().replace(/^@/, '');
    try { username = decodeURIComponent(username); } catch (_) { }

    return /^[A-Za-z0-9._]{1,64}$/.test(username) ? username : null;
  }

  let _tiktokHydrationItems = null;

  function getTikTokHydrationItem(itemId) {
    if (!itemId) return null;

    if (_tiktokHydrationItems === null) {
      _tiktokHydrationItems = new Map();

      try {
        const script = document.getElementById?.('__UNIVERSAL_DATA_FOR_REHYDRATION__');
        const json = script?.textContent ? JSON.parse(script.textContent) : null;
        const scope = json?.__DEFAULT_SCOPE__ || json;
        const items = scope?.['webapp.updated-items'];

        if (Array.isArray(items)) {
          for (const item of items) {
            const id = String(item?.id || item?.video?.id || '');
            if (/^\d{15,22}$/.test(id)) _tiktokHydrationItems.set(id, item);
          }
        }
      } catch (_) { }
    }

    return _tiktokHydrationItems.get(String(itemId)) || null;
  }

  function getMediaTitle(video, contextNode = video) {
    const site = detectSite(location.hostname);

    if (site === 'instagram') {
      const existingTitle = cleanMediaTitle(
        video?.getAttribute?.(INSTAGRAM_TITLE_ATTRIBUTE)
        || contextNode?.getAttribute?.(INSTAGRAM_TITLE_ATTRIBUTE)
      );
      if (existingTitle) return existingTitle;

      const metadata = requestInstagramMetadata(video, contextNode);
      return metadata?.title || document.title || 'Instagram Reel';
    }

    if (site !== 'tiktok') return document.title || 'video';

    const roots = collectTikTokRoots(video, contextNode);
    const itemId = findTikTokItemId(roots);
    const hydrationItem = getTikTokHydrationItem(itemId);

    for (const root of roots) {
      if (!root) continue;

      const description = cleanMediaTitle(
        root.querySelector?.('[data-e2e="video-desc"]')?.textContent
        || root.querySelector?.('img[alt]')?.getAttribute?.('alt')
      );
      if (description) return description;
    }

    const hydrationDescription = cleanMediaTitle(hydrationItem?.desc);
    if (hydrationDescription) return hydrationDescription;

    const username = findTikTokUsername(roots)
      || normalizeTikTokUsername(hydrationItem?.author?.uniqueId);
    if (username) return `TikTok - @${username}`;

    return document.title || 'TikTok video';
  }

  function cleanMediaTitle(value) {
    return String(value || '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, 160) || null;
  }

  function canonicalizeFacebookGroupUrl(url) {
    const match = url.pathname.match(/^\/groups\/([^/]+)\/?$/i);
    const rawPostIds = url.searchParams.get('multi_permalinks');
    if (!match || !rawPostIds) return;

    const postId = rawPostIds.match(/\d{5,}/)?.[0];
    if (!postId) return;

    url.pathname = `/groups/${match[1]}/permalink/${postId}/`;
    url.search = '';
  }

  function detectSite(hostname) {
    const host = String(hostname || '').toLowerCase();
    if (host === 'tiktok.com' || host.endsWith('.tiktok.com')) return 'tiktok';
    if (host === 'facebook.com' || host.endsWith('.facebook.com') || host === 'fb.watch') return 'facebook';
    if (host === 'instagram.com' || host.endsWith('.instagram.com')) return 'instagram';
    return null;
  }

  function collectSearchRoots(video, contextNode, site) {
    const roots = [];
    const seen = new Set();

    const add = (element, level, bonus = 0) => {
      if (!element || seen.has(element)) return;
      if (element === document.body || element === document.documentElement) return;
      seen.add(element);
      roots.push({ element, level, bonus });
    };

    add(contextNode, 0, 35);
    add(video, 0, 40);

    const selector = SITE_CONTAINERS[site];
    if (selector) {
      add(safeClosest(contextNode, selector), 0, 60);
      add(safeClosest(video, selector), 0, 65);
    }

    let element = video;
    for (let level = 0; level < 26 && element; level++, element = element.parentElement) {
      const semanticBonus = isSemanticContainer(element) ? 25 : 0;
      add(element, level, semanticBonus);
      if (level > 0 && isFeedBoundary(element, site)) break;
    }

    element = contextNode;
    for (let level = 0; level < 12 && element; level++, element = element.parentElement) {
      add(element, level, 10);
      if (level > 0 && isFeedBoundary(element, site)) break;
    }

    return roots;
  }

  function collectCandidates(roots, video, site) {
    const bestByUrl = new Map();
    const videoRect = safeRect(video);
    const videoContainer = safeClosest(video, SITE_CONTAINERS[site]);

    for (const rootInfo of roots) {
      const { element, level, bonus } = rootInfo;
      const anchors = [];

      if (isAnchor(element)) anchors.push(element);

      try {
        const found = element.querySelectorAll?.('a[href]');
        if (found) {
          for (let i = 0; i < found.length && i < 500; i++) anchors.push(found[i]);
        }
      } catch (_) { }

      for (const anchor of anchors) {
        const rawUrl = anchor.href || anchor.getAttribute?.('href');
        const url = normalizeUrl(rawUrl, site);
        if (!url) continue;

        const quality = getUrlQuality(url, site);
        if (quality <= 0) continue;

        let score = quality + bonus + Math.max(0, 30 - (level * 2));

        if (anchor === element) score += 20;
        if (anchor.contains?.(video)) score += 35;
        if (video.contains?.(anchor)) score += 10;

        const anchorContainer = safeClosest(anchor, SITE_CONTAINERS[site]);
        if (videoContainer && anchorContainer === videoContainer) score += 45;

        score += getSemanticAnchorBonus(anchor, site);
        score += getProximityBonus(videoRect, safeRect(anchor));

        const previous = bestByUrl.get(url);
        if (!previous || score > previous.score) {
          bestByUrl.set(url, { url, score });
        }
      }

      collectAttributeUrls(element, site, level, bonus, bestByUrl);
    }

    return [...bestByUrl.values()];
  }

  function collectAttributeUrls(element, site, level, bonus, bestByUrl) {
    let nodes = [];
    try {
      nodes = element.querySelectorAll?.('[data-lynx-uri],[data-url]') || [];
    } catch (_) { }

    for (let i = 0; i < nodes.length && i < 100; i++) {
      const node = nodes[i];
      const rawUrl = node.getAttribute?.('data-lynx-uri') || node.getAttribute?.('data-url');
      const url = normalizeUrl(rawUrl, site);
      if (!url) continue;

      const quality = getUrlQuality(url, site);
      if (quality <= 0) continue;

      const score = quality + bonus + Math.max(0, 18 - level);
      const previous = bestByUrl.get(url);
      if (!previous || score > previous.score) {
        bestByUrl.set(url, { url, score });
      }
    }
  }

  function normalizeUrl(rawUrl, site) {
    if (!rawUrl) return null;

    let url;
    try {
      url = new URL(rawUrl, location.href);
    } catch (_) {
      return null;
    }

    if (!/^https?:$/.test(url.protocol)) return null;

    if (site === 'facebook' && url.hostname === 'l.facebook.com' && url.pathname === '/l.php') {
      const target = url.searchParams.get('u');
      if (target) {
        try { url = new URL(target); } catch (_) { return null; }
      }
    }

    if (!isAllowedHost(url.hostname, site)) return null;

    if (site === 'facebook') {
      canonicalizeFacebookGroupUrl(url);
    }

    url.protocol = 'https:';
    url.hash = '';

    for (const key of [...url.searchParams.keys()]) {
      if (TRACKING_PARAMS.has(key)) url.searchParams.delete(key);
    }

    if (site === 'facebook' && url.hostname !== 'fb.watch') {
      url.hostname = 'www.facebook.com';
    } else if (site === 'tiktok') {
      url.hostname = 'www.tiktok.com';
    } else if (site === 'instagram') {
      url.hostname = 'www.instagram.com';
    }

    url.pathname = url.pathname.replace(/\/{2,}/g, '/');
    return url.href;
  }

  function isAllowedHost(hostname, site) {
    const host = String(hostname || '').toLowerCase();
    if (site === 'facebook') {
      return host === 'facebook.com' || host.endsWith('.facebook.com') || host === 'fb.watch';
    }
    if (site === 'tiktok') {
      return host === 'tiktok.com' || host.endsWith('.tiktok.com');
    }
    if (site === 'instagram') {
      return host === 'instagram.com' || host.endsWith('.instagram.com');
    }
    return false;
  }

  function getUrlQuality(urlString, site) {
    let url;
    try { url = new URL(urlString); } catch (_) { return 0; }

    const path = url.pathname.replace(/\/{2,}/g, '/');

    if (site === 'tiktok') {
      if (/^\/@[^/]+\/video\/\d+\/?$/i.test(path)) return 160;
      if (/\/video\/\d+\/?$/i.test(path)) return 150;
      if (/^\/t\/[A-Za-z0-9_-]+\/?$/i.test(path)) return 120;
      return 0;
    }

    if (site === 'facebook') {
      if (url.hostname === 'fb.watch' && /^\/[A-Za-z0-9_-]+\/?$/i.test(path)) return 165;
      if (/^\/reel\/[A-Za-z0-9_-]+\/?$/i.test(path)) return 170;
      if (/^\/share\/(?:r|v)\/[A-Za-z0-9_-]+\/?$/i.test(path)) return 160;
      if (url.searchParams.get('v') && /^\/watch(?:\/live)?\/?$/i.test(path)) return 155;
      if (url.searchParams.get('v') && /^\/video\.php$/i.test(path)) return 150;
      if (/\/videos\/(?:[^/]+\/)?[A-Za-z0-9_-]+\/?$/i.test(path)) return 150;
      if (/\/posts\/(?:pfbid[A-Za-z0-9]+|\d+)\/?$/i.test(path)) return 135;
      if (/^\/(?:permalink|story)\.php$/i.test(path) && url.searchParams.get('story_fbid')) return 135;
      if (/^\/groups\/[^/]+\/(?:posts|permalink)\/[^/]+\/?$/i.test(path)) return 145;
      return 0;
    }

    if (site === 'instagram') {
      if (/^\/(?:reel|p|tv)\/[A-Za-z0-9_-]+\/?$/i.test(path)) return 150;
      return 0;
    }

    return 0;
  }

  function getCanonicalUrl(site) {
    const selectors = [
      'link[rel="canonical"][href]',
      'meta[property="og:url"][content]'
    ];

    for (const selector of selectors) {
      const node = document.querySelector?.(selector);
      const rawUrl = node?.href || node?.content;
      const url = normalizeUrl(rawUrl, site);
      if (url) return url;
    }

    return null;
  }

  function getSemanticAnchorBonus(anchor, site) {
    const text = [
      anchor.textContent,
      anchor.getAttribute?.('aria-label'),
      anchor.getAttribute?.('title')
    ].filter(Boolean).join(' ').toLowerCase();

    let score = 0;
    if (/video|reel|watch|permalink|xem|phút|giờ|ngày|ago/.test(text)) score += 10;
    if (site === 'tiktok' && /@/.test(text)) score += 4;
    return score;
  }

  function getProximityBonus(videoRect, anchorRect) {
    if (!videoRect || !anchorRect) return 0;

    const dx = Math.max(0,
      videoRect.left - anchorRect.right,
      anchorRect.left - videoRect.right
    );
    const dy = Math.max(0,
      videoRect.top - anchorRect.bottom,
      anchorRect.top - videoRect.bottom
    );

    const distance = Math.hypot(dx, dy);
    return Math.max(0, 25 - (distance / 80));
  }

  function isFeedBoundary(element, site) {
    if (!element) return false;

    if (site === 'facebook') {
      return element.tagName === 'ARTICLE'
        || element.getAttribute?.('role') === 'article'
        || element.hasAttribute?.('aria-posinset')
        || /^FeedUnit/i.test(element.getAttribute?.('data-pagelet') || '');
    }

    if (site === 'tiktok') {
      try {
        return element.matches?.('[data-e2e="recommend-list-item-container"],article') || false;
      } catch (_) {
        return false;
      }
    }

    return element.tagName === 'ARTICLE' || element.getAttribute?.('role') === 'article';
  }

  function isSemanticContainer(element) {
    const role = element.getAttribute?.('role');
    const tag = element.tagName;
    return tag === 'ARTICLE' || role === 'article' || role === 'listitem';
  }

  function isAnchor(element) {
    return element?.tagName === 'A' && !!(element.href || element.getAttribute?.('href'));
  }

  function safeClosest(element, selector) {
    if (!element || !selector) return null;
    try { return element.closest?.(selector) || null; } catch (_) { return null; }
  }

  function safeRect(element) {
    try {
      const rect = element?.getBoundingClientRect?.();
      if (!rect || (!rect.width && !rect.height)) return null;
      return rect;
    } catch (_) {
      return null;
    }
  }

  PD.SiteUrlResolver = { resolve, getMediaTitle };
})(globalThis);
