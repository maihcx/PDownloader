(function (root) {
  const PD = root.PD || (root.PD = {});

  const MAX_CANDIDATES_PER_TAB = 80;
  const CANDIDATE_TTL_MS = 30 * 60 * 1000;
  const STORAGE_PREFIX = 'pdMediaCandidates:';
  const candidatesByTab = new Map();
  const changeListeners = new Set();
  const persistTimers = new Map();
  const clearedDuringRestore = new Set();
  let restoreCompleted = false;
  let nextId = 1;

  function normalizeUrl(url) {
    try {
      const parsed = new URL(url);
      parsed.hash = '';
      return parsed.href;
    } catch (_) {
      return String(url || '').split('#')[0];
    }
  }

  function cloneCandidate(candidate) {
    return candidate ? { ...candidate, requestHeaders: { ...(candidate.requestHeaders || {}) } } : null;
  }

  function prune(tabId) {
    const list = candidatesByTab.get(tabId);
    if (!list) return;

    const cutoff = Date.now() - CANDIDATE_TTL_MS;
    const alive = list
      .filter(item => item.lastSeenAt >= cutoff)
      .sort((a, b) => (b.score - a.score) || (b.lastSeenAt - a.lastSeenAt))
      .slice(0, MAX_CANDIDATES_PER_TAB);

    if (alive.length) candidatesByTab.set(tabId, alive);
    else candidatesByTab.delete(tabId);
  }

  function makeKey(candidate) {
    const normalized = normalizeUrl(candidate.url);
    return `${candidate.mediaType || 'unknown'}|${candidate.kind || 'direct'}|${normalized}`;
  }

  function storageKey(tabId) {
    return `${STORAGE_PREFIX}${tabId}`;
  }

  function schedulePersist(tabId) {
    if (!chrome.storage?.session || persistTimers.has(tabId)) return;

    persistTimers.set(tabId, setTimeout(() => {
      persistTimers.delete(tabId);
      const list = candidatesByTab.get(tabId) || [];
      chrome.storage.session.set({ [storageKey(tabId)]: list }).catch(() => {});
    }, 250));
  }

  async function restoreSession() {
    if (!chrome.storage?.session) {
      restoreCompleted = true;
      return;
    }

    try {
      const stored = await chrome.storage.session.get(null);
      for (const [key, value] of Object.entries(stored || {})) {
        if (!key.startsWith(STORAGE_PREFIX) || !Array.isArray(value)) continue;
        const tabId = Number(key.slice(STORAGE_PREFIX.length));
        if (!Number.isInteger(tabId) || tabId < 0 || clearedDuringRestore.has(tabId)) continue;

        const cutoff = Date.now() - CANDIDATE_TTL_MS;
        const list = value
          .filter(item => item?.url && (item.lastSeenAt || 0) >= cutoff)
          .slice(0, MAX_CANDIDATES_PER_TAB);
        if (list.length && !candidatesByTab.has(tabId)) candidatesByTab.set(tabId, list);
      }
    } catch (_) {
    } finally {
      restoreCompleted = true;
      clearedDuringRestore.clear();
    }
  }

  function register(tabId, rawCandidate) {
    if (!Number.isInteger(tabId) || tabId < 0 || !rawCandidate?.url) return null;

    const url = normalizeUrl(rawCandidate.url);
    if (!/^https?:/i.test(url)) return null;

    prune(tabId);
    const list = candidatesByTab.get(tabId) || [];
    const now = Date.now();
    const candidate = {
      id: rawCandidate.id || `media-${tabId}-${Date.now()}-${nextId++}`,
      url,
      mediaType: rawCandidate.mediaType || 'unknown',
      kind: rawCandidate.kind || 'direct',
      mime: rawCandidate.mime || '',
      extension: rawCandidate.extension || '',
      filename: rawCandidate.filename || '',
      title: rawCandidate.title || '',
      size: Number(rawCandidate.size) || 0,
      referer: rawCandidate.referer || rawCandidate.pageUrl || '',
      pageUrl: rawCandidate.pageUrl || rawCandidate.referer || '',
      source: rawCandidate.source || 'unknown',
      requestType: rawCandidate.requestType || '',
      score: Number(rawCandidate.score) || 0,
      likelySegment: !!rawCandidate.likelySegment,
      route: rawCandidate.route || '',
      requestHeaders: { ...(rawCandidate.requestHeaders || {}) },
      foundAt: Number(rawCandidate.foundAt) || now,
      lastSeenAt: now
    };

    const key = makeKey(candidate);
    const existing = list.find(item => makeKey(item) === key);

    let stored;
    if (existing) {
      const originalId = existing.id;
      Object.assign(existing, candidate, {
        id: originalId,
        foundAt: Math.min(existing.foundAt || now, candidate.foundAt || now),
        lastSeenAt: now,
        score: Math.max(existing.score || 0, candidate.score || 0),
        size: Math.max(existing.size || 0, candidate.size || 0),
        filename: candidate.filename || existing.filename || '',
        title: candidate.title || existing.title || '',
        referer: candidate.referer || existing.referer || '',
        pageUrl: candidate.pageUrl || existing.pageUrl || '',
        requestHeaders: {
          ...(existing.requestHeaders || {}),
          ...(candidate.requestHeaders || {})
        }
      });
      stored = existing;
    } else {
      list.push(candidate);
      stored = candidate;
    }

    list.sort((a, b) => (b.score - a.score) || (b.lastSeenAt - a.lastSeenAt));
    if (list.length > MAX_CANDIDATES_PER_TAB) list.length = MAX_CANDIDATES_PER_TAB;
    candidatesByTab.set(tabId, list);
    schedulePersist(tabId);

    for (const listener of changeListeners) {
      try { listener(tabId, cloneCandidate(stored)); } catch (_) { }
    }

    return cloneCandidate(stored);
  }

  function getAll(tabId, options = {}) {
    if (!Number.isInteger(tabId) || tabId < 0) return [];
    prune(tabId);

    const minScore = Number.isFinite(options.minScore) ? options.minScore : -Infinity;
    const mediaType = options.mediaType || '';
    const includeSegments = options.includeSegments === true;

    return (candidatesByTab.get(tabId) || [])
      .filter(item => item.score >= minScore)
      .filter(item => !mediaType || item.mediaType === mediaType || item.mediaType === 'manifest')
      .filter(item => includeSegments || !item.likelySegment)
      .map(cloneCandidate);
  }

  function getBest(tabId, options = {}) {
    return getAll(tabId, options)[0] || null;
  }

  function getById(tabId, id) {
    if (!id) return null;
    prune(tabId);
    return cloneCandidate((candidatesByTab.get(tabId) || []).find(item => item.id === id));
  }

  function clear(tabId) {
    if (!restoreCompleted) clearedDuringRestore.add(tabId);
    candidatesByTab.delete(tabId);
    const timer = persistTimers.get(tabId);
    if (timer) clearTimeout(timer);
    persistTimers.delete(tabId);
    chrome.storage?.session?.remove(storageKey(tabId)).catch(() => {});
  }

  function onChange(listener) {
    changeListeners.add(listener);
    return () => changeListeners.delete(listener);
  }

  PD.MediaCandidateRegistry = {
    register,
    getAll,
    getBest,
    getById,
    clear,
    onChange,
    restoreSession
  };
})(self);
