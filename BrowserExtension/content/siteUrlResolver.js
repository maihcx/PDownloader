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
    if (current && getUrlQuality(current, site) > 0) return current;

    const directVideoUrl = getDirectVideoUrl(video, contextNode, site);
    if (directVideoUrl) return directVideoUrl;

    const roots = collectSearchRoots(video, contextNode, site);
    const candidates = collectCandidates(roots, video, site);

    if (candidates.length > 0) {
      candidates.sort((a, b) => b.score - a.score);
      return candidates[0].url;
    }

    const canonical = getCanonicalUrl(site);
    if (canonical && getUrlQuality(canonical, site) > 0) return canonical;

    return current || location.href;
  }

  function getDirectVideoUrl(video, contextNode, site) {
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

  PD.SiteUrlResolver = { resolve };
})(globalThis);
