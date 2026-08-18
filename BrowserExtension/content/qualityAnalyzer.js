(function (root) {
  const PD = root.PD || (root.PD = {});
  if (PD.QualityAnalyzer) return;

  const CACHE_TTL = 5 * 60 * 1000;
  const cache = new Map();
  const pending = new Map();

  function ensureTheme() {
    if (document.querySelector('link[data-pd-theme="1"]')) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = PDWebExt.runtime.getURL('common/theme.css');
    link.dataset.pdTheme = '1';
    (document.head || document.documentElement).appendChild(link);
  }

  function ensureStyle() {
    if (document.getElementById('pd-quality-style')) return;

    const style = document.createElement('style');
    style.id = 'pd-quality-style';
    style.textContent = `
.pd-quality-panel {
  position: absolute;
  top: 12px; right: 12px;
  z-index: 2147483647;
  font-family: 'Segoe UI', system-ui, sans-serif;
  user-select: none;
  background: var(--pd-bg, rgba(13,17,23,.88));
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border: 1px solid var(--pd-border, rgba(79,195,247,.25));
  border-radius: 10px;
  box-shadow: 0 6px 28px var(--pd-shadow, rgba(0,0,0,.45)),
              0 0 0 1px rgba(79,195,247,.07);
  padding: 4px 6px;
  display: flex;
  align-items: center;
  height: 36px;
  box-sizing: border-box;
  gap: 2px;
  color: var(--pd-text, #e6edf3);
}
.pd-quality-panel.pd-quality-fixed { position: fixed; top: auto; right: auto; }
.pd-quality-panel.pd-quality-shorts { right: auto; left: 12px; }

.pd-quality-main-btn {
  display: flex; align-items: center; gap: 8px;
  background: transparent; border: none;
  color: var(--pd-text, #e6edf3); font-size: 13px; font-weight: 600;
  font-family: inherit; padding: 0 10px;
  cursor: pointer; height: 100%;
  border-radius: 7px; transition: background .18s, color .15s, transform .1s;
  box-sizing: border-box;
}
.pd-quality-main-btn:hover { background: var(--pd-accent-bg, rgb(0,30,48)); color: var(--pd-text, #e6edf3); }
.pd-quality-main-btn:active { transform: scale(.97); }
.pd-quality-main-btn:disabled { cursor: wait; opacity: .9; }

.pd-quality-icon {
  width: 0; height: 0;
  border-left: 10px solid var(--pd-accent, #4fc3f7);
  border-top: 6px solid transparent;
  border-bottom: 6px solid transparent;
  display: inline-block;
}
.pd-quality-sep { width: 1px; height: 18px; background: var(--pd-border2, rgba(255,255,255,.08)); margin: 0 2px; }
.pd-quality-close {
  background: transparent; border: none;
  color: var(--pd-muted, #8b949e); font-size: 13px; padding: 0 8px;
  cursor: pointer; display: flex; align-items: center; justify-content: center;
  height: 100%; border-radius: 7px; transition: background .18s, color .15s;
  font-weight: 600; box-sizing: border-box;
}
.pd-quality-close:hover { background: var(--pd-border2, rgba(255,255,255,.08)); color: var(--pd-text, #e6edf3); }

.pd-quality-dropdown {
  position: absolute;
  top: calc(100% + 8px); right: 0;
  width: 460px; max-width: calc(100vw - 16px); max-height: min(380px, calc(100vh - 64px));
  background: var(--pd-dropdown, rgba(13,17,23,.95));
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border: 1px solid var(--pd-border, rgba(79,195,247,.25));
  border-radius: 12px;
  box-shadow: 0 16px 48px var(--pd-shadow, rgba(0,0,0,.45));
  padding: 10px; display: none;
  flex-direction: column; gap: 6px;
  z-index: 2147483647; overflow: hidden; box-sizing: border-box;
}
.pd-quality-dropdown.open { display: flex; }
.pd-quality-panel.pd-quality-align-left .pd-quality-dropdown,
.pd-quality-panel.pd-quality-shorts .pd-quality-dropdown { right: auto; left: 0; }

.pd-quality-search {
  width: 100%;
  background: var(--pd-border2, rgba(255,255,255,.08));
  border: 1px solid var(--pd-border, rgba(79,195,247,.25));
  color: var(--pd-text, #e6edf3); padding: 7px 12px;
  border-radius: 8px; font-size: 13px; font-family: inherit; outline: none;
  transition: border-color .2s, background .2s; box-sizing: border-box;
}
.pd-quality-search:focus { border-color: var(--pd-accent, #4fc3f7); background: var(--pd-accent-bg, rgb(0,30,48)); }
.pd-quality-search::placeholder { color: var(--pd-muted, #8b949e); }

.pd-quality-filters {
  display: flex; gap: 6px; padding-bottom: 8px;
  border-bottom: 1px solid var(--pd-border2, rgba(255,255,255,.08));
  flex-shrink: 0; flex-wrap: wrap;
}
.pd-quality-filter-btn {
  background: var(--pd-border2, rgba(255,255,255,.08));
  border: 1px solid var(--pd-border2, rgba(255,255,255,.08));
  color: var(--pd-muted, #8b949e); padding: 5px 12px;
  border-radius: 6px; font-size: 12px; font-weight: 600;
  cursor: pointer; font-family: inherit; transition: all .18s;
}
.pd-quality-filter-btn:hover { border-color: var(--pd-accent, #4fc3f7); color: var(--pd-text, #e6edf3); }
.pd-quality-filter-btn.active { background: var(--pd-accent-bg, rgb(0,30,48)); border-color: var(--pd-accent, #4fc3f7); color: var(--pd-text, #e6edf3); }

.pd-quality-list { display: flex; flex-direction: column; gap: 2px; overflow-y: auto; flex: 1; min-height: 0; }
.pd-quality-empty { color: var(--pd-muted, #8b949e); font-size: 13px; padding: 20px; text-align: center; }
.pd-quality-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 8px 14px; color: var(--pd-text, #e6edf3); font-size: 13px;
  cursor: pointer; border-radius: 8px; transition: background .15s;
}
.pd-quality-item:hover { background: var(--pd-accent-bg, rgb(0,30,48)); color: var(--pd-text, #e6edf3); }
.pd-quality-size { color: var(--pd-accent, #4fc3f7); font-size: 12px; font-weight: 700; margin-left: 12px; flex-shrink: 0; }

.pd-quality-spinner {
  width: 14px; height: 14px;
  border: 2px solid var(--pd-border2, rgba(255,255,255,.15));
  border-top-color: var(--pd-accent, #4fc3f7);
  border-radius: 50%; animation: pd-quality-spin .6s linear infinite; display: inline-block;
}
@keyframes pd-quality-spin { to { transform: rotate(360deg); } }

.pd-quality-toast {
  position: absolute; top: calc(100% + 8px); right: 0;
  background: var(--pd-green-bg, rgba(76,175,80,.15)); color: var(--pd-green, #4caf50);
  border: 1px solid var(--pd-green, #4caf50);
  font-size: 13px; padding: 7px 16px; border-radius: 8px; white-space: nowrap;
  box-shadow: 0 6px 20px var(--pd-shadow, rgba(0,0,0,.45));
  animation: pd-quality-toast-in 2.8s forwards; pointer-events: none; z-index: 2147483647;
}
.pd-quality-toast.err { background: var(--pd-red-bg, rgba(244,67,54,.92)); color: #fff; border-color: var(--pd-red, #f44336); }
@keyframes pd-quality-toast-in {
  0% { opacity:0; transform: translateY(-4px); }
  10% { opacity:1; transform: translateY(0); }
  88% { opacity:1; }
  100% { opacity:0; }
}
`;
    (document.head || document.documentElement).appendChild(style);
  }

  function sanitizeName(value, fallback = 'video') {
    return PD.MediaTitle?.sanitize(value, fallback, 100) || fallback;
  }

  function getCacheKey(context) {
    return String(context?.cacheKey || context?.url || '').trim();
  }

  async function analyze(context, force = false) {
    const url = String(context?.url || '').trim();
    if (!/^https?:\/\//i.test(url)) {
      return { success: false, error: PD.I18n.t('ytCannotAnalyze') };
    }

    const key = getCacheKey(context);
    const cached = cache.get(key);
    if (!force && cached && Date.now() - cached.time < CACHE_TTL) return cached.data;
    if (!force && pending.has(key)) return pending.get(key);

    const request = PDWebExt.runtime.sendMessage({
      action: 'analyze_media',
      url,
      referer: context?.referer || location.href,
      headers: context?.headers || undefined
    }).then(data => {
      if (data?.success) cache.set(key, { time: Date.now(), data });
      return data;
    }).finally(() => pending.delete(key));

    pending.set(key, request);
    return request;
  }

  function candidateFromContext(context) {
    const directMediaUrl = String(context?.mediaUrl || '').trim();
    const rawUrl = /^https?:\/\//i.test(directMediaUrl)
      ? directMediaUrl
      : String(context?.url || '').trim();
    if (!/^https?:\/\//i.test(rawUrl)) return null;

    let extension = '';
    try {
      const pathname = new URL(rawUrl).pathname;
      extension = pathname.match(/\.([A-Za-z0-9]{2,8})$/)?.[1]?.toLowerCase() || '';
    } catch (_) { }

    const manifestKind = extension === 'm3u8' ? 'hls' : extension === 'mpd' ? 'dash' : '';
    const isDirectVideo = ['mp4', 'webm', 'mkv', 'mov', 'm4v', 'avi', 'flv', 'wmv', 'mpeg', 'mpg', 'ogv']
      .includes(extension);
    const isCurrentMediaUrl = rawUrl === directMediaUrl;
    if (!manifestKind && !isDirectVideo && !isCurrentMediaUrl) return null;

    return {
      url: rawUrl,
      mediaType: manifestKind ? 'manifest' : 'video',
      kind: manifestKind || 'direct',
      extension,
      title: context?.title || '',
      referer: context?.referer || location.href,
      pageUrl: context?.referer || location.href,
      requestHeaders: { ...(context?.headers || {}) }
    };
  }

  function normalizeComparableUrl(value) {
    try {
      const url = new URL(String(value || ''), location.href);
      url.hash = '';
      return url.href;
    } catch (_) {
      return String(value || '').split('#')[0];
    }
  }

  function candidateRelationScore(candidate, context, activeUrls) {
    const candidateUrl = normalizeComparableUrl(candidate?.url);
    const contextUrl = normalizeComparableUrl(context?.url);
    const mediaUrl = normalizeComparableUrl(context?.mediaUrl);
    const contextPage = normalizeComparableUrl(context?.referer || location.href);
    const candidatePage = normalizeComparableUrl(candidate?.pageUrl || candidate?.referer);
    let score = 0;

    if (candidateUrl && candidateUrl === mediaUrl) score += 20_000;
    if (candidateUrl && activeUrls.has(candidateUrl)) score += 15_000;
    if (candidateUrl && candidateUrl === contextUrl) score += 10_000;
    if (candidatePage && candidatePage === contextPage) score += 1_000;

    try {
      if (candidateUrl && contextUrl
          && new URL(candidateUrl).origin === new URL(contextUrl).origin) {
        score += 500;
      }
    } catch (_) { }

    return score;
  }

  function selectContextCandidate(candidates, context, playback) {
    const sourceCandidates = candidates || [];
    const activeUrls = new Set((playback?.activeVideoUrls || [])
      .map(normalizeComparableUrl)
      .filter(Boolean));
    const related = sourceCandidates
      .filter(candidate => /^https?:\/\//i.test(candidate?.url || ''))
      .map(candidate => ({
        candidate,
        relation: candidateRelationScore(candidate, context, activeUrls),
        freshness: Number(candidate.lastSeenAt || candidate.foundAt || 0)
      }))
      .filter(item => item.relation >= 10_000
        || (sourceCandidates.length === 1 && item.relation > 0))
      .sort((a, b) => (b.relation - a.relation) || (b.freshness - a.freshness));

    return related[0]?.candidate || null;
  }

  async function downloadDirectFallback(context) {
    if (context?.allowDirectFallback !== true) return null;

    try {
      const detected = await PDWebExt.runtime.sendMessage({
        action: 'get_media_candidates',
        mediaType: 'video',
        minScore: 45
      });

      let candidate = selectContextCandidate(
        detected?.candidates,
        context,
        detected?.playback
      );
      candidate ||= candidateFromContext(context);

      if (!candidate?.url) return null;

      return await PDWebExt.runtime.sendMessage({
        action: 'download_media_candidate',
        candidateId: candidate.id || '',
        preferredUrl: candidate.url,
        candidate,
        mediaType: 'video'
      });
    } catch (_) {
      return { success: false };
    }
  }

  function showToast(panel, message, error = false) {
    panel.querySelectorAll('.pd-quality-toast').forEach(item => item.remove());
    const toast = document.createElement('div');
    toast.className = 'pd-quality-toast' + (error ? ' err' : '');
    toast.textContent = message;
    panel.appendChild(toast);
    setTimeout(() => toast.remove(), 2800);
  }

  function getFormatKind(format) {
    const note = String(format?.note || '').toLowerCase();
    if (note === 'audio only') return 'audio';
    if (note === 'video only') return 'video';
    return 'muxed';
  }

  function renderDropdown(dropdown, data, context, panel) {
    dropdown.replaceChildren();
    let filter = 'all';
    let query = '';

    const search = document.createElement('input');
    search.className = 'pd-quality-search';
    search.placeholder = PD.I18n.t('ytSearchPlaceholder');
    search.addEventListener('input', event => {
      query = event.target.value.toLowerCase();
      draw();
    });
    for (const eventName of ['keydown', 'keyup', 'keypress']) {
      search.addEventListener(eventName, event => event.stopPropagation());
    }
    dropdown.appendChild(search);

    const filterBar = document.createElement('div');
    filterBar.className = 'pd-quality-filters';
    const filters = [
      ['all', 'ytFilterAll'],
      ['muxed', 'ytFilterMuxed'],
      ['video', 'ytFilterVideo'],
      ['audio', 'ytFilterAudio']
    ];

    for (const [value, labelKey] of filters) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'pd-quality-filter-btn' + (value === 'all' ? ' active' : '');
      button.dataset.filter = value;
      button.textContent = PD.I18n.t(labelKey);
      button.addEventListener('click', event => {
        event.stopPropagation();
        filterBar.querySelectorAll('.pd-quality-filter-btn').forEach(item => item.classList.remove('active'));
        button.classList.add('active');
        filter = value;
        draw();
      });
      filterBar.appendChild(button);
    }
    dropdown.appendChild(filterBar);

    const list = document.createElement('div');
    list.className = 'pd-quality-list';
    dropdown.appendChild(list);

    function draw() {
      list.replaceChildren();
      const formats = (data.formats || []).filter(format => {
        const kind = getFormatKind(format);
        if (filter === 'muxed' && kind === 'audio') return false;
        if (filter === 'video' && kind !== 'video') return false;
        if (filter === 'audio' && kind !== 'audio') return false;
        if (!query) return true;
        const label = [
          format.height ? `${format.height}p` : 'audio',
          format.ext || '', format.note || '', format.size || ''
        ].join(' ').toLowerCase();
        return label.includes(query);
      });

      if (!formats.length) {
        const empty = document.createElement('div');
        empty.className = 'pd-quality-empty';
        empty.textContent = PD.I18n.t('ytNoFormats');
        list.appendChild(empty);
        return;
      }

      for (const format of formats) {
        const item = document.createElement('div');
        item.className = 'pd-quality-item';

        const kind = getFormatKind(format);
        const quality = format.height ? `${format.height}p` : 'Audio';
        const ext = (format.ext || 'mp4').toUpperCase();
        let note = format.note ? ` · ${format.note}` : '';
        if (kind === 'video' && filter !== 'video') note = ' · ' + PD.I18n.t('ytFilterMuxed');

        const label = document.createElement('span');
        label.textContent = `${quality} ${ext}${note}`;
        const size = document.createElement('span');
        size.className = 'pd-quality-size';
        size.textContent = format.size || '–';
        item.append(label, size);

        item.addEventListener('click', async event => {
          event.stopPropagation();
          dropdown.classList.remove('open');

          let formatId = format.id;
          if (kind === 'video' && filter !== 'video') formatId += '+bestaudio';

          const isManifest = /\.(?:m3u8|mpd)(?:$|[?#])/i.test(context?.url || '');
          const title = PD.MediaTitle?.resolve({
            isManifest,
            analyzedTitle: data.title,
            contextTitle: context?.title,
            pageTitle: data.pageTitle,
            pageUrl: data.pageUrl,
            mediaUrl: context?.url,
            fallback: 'video'
          }) || context?.title || data.title || 'video';
          const filename = `${sanitizeName(title)}_${quality}.${format.ext || 'mp4'}`;
          const response = await PDWebExt.runtime.sendMessage({
            action: 'download_media_format',
            url: context.url,
            formatId,
            filename,
            title,
            filesize: format.filesize || 0,
            referer: context?.referer || location.href,
            headers: context?.headers || undefined
          });

          showToast(
            panel,
            response?.success ? PD.I18n.t('ytAddedToQueue') : (response?.error || PD.I18n.t('ytDownloadError')),
            !response?.success
          );
        });
        list.appendChild(item);
      }
    }

    draw();
  }

  function createPanel(options = {}) {
    ensureTheme();
    ensureStyle();

    const panel = document.createElement('div');
    panel.className = ['pd-quality-panel', 'pd-theme-root', options.fixed ? 'pd-quality-fixed' : '', options.className || '']
      .filter(Boolean).join(' ');

    const mainButton = document.createElement('button');
    mainButton.type = 'button';
    mainButton.className = 'pd-quality-main-btn';

    const separator = document.createElement('div');
    separator.className = 'pd-quality-sep';

    const closeButton = document.createElement('button');
    closeButton.type = 'button';
    closeButton.className = 'pd-quality-close';
    closeButton.title = PD.I18n.t('ytClose');
    closeButton.textContent = '✕';

    const dropdown = document.createElement('div');
    dropdown.className = 'pd-quality-dropdown';

    panel.append(mainButton, separator, closeButton, dropdown);

    let contextProvider = options.getContext || (() => null);
    let outsideHandler = null;
    let contextRevision = 0;

    function setLoading(loading) {
      const icon = document.createElement(loading ? 'div' : 'span');
      icon.className = loading ? 'pd-quality-spinner' : 'pd-quality-icon';
      const label = document.createElement('span');
      label.className = 'pd-quality-label';
      label.textContent = PD.I18n.t(loading ? 'ytAnalyzing' : 'ytDownloadThisVideo');
      mainButton.replaceChildren(icon, label);
      mainButton.disabled = loading;
    }

    function closeDropdown() {
      dropdown.classList.remove('open');
      if (outsideHandler) document.removeEventListener('click', outsideHandler, true);
      outsideHandler = null;
    }

    function invalidateContext() {
      contextRevision++;
      closeDropdown();
      setLoading(false);
    }

    function openDropdown(data, context) {
      renderDropdown(dropdown, data, context, panel);
      dropdown.classList.add('open');
      if (outsideHandler) document.removeEventListener('click', outsideHandler, true);
      outsideHandler = event => {
        if (!panel.contains(event.target)) closeDropdown();
      };
      setTimeout(() => document.addEventListener('click', outsideHandler, true), 0);
    }

    setLoading(false);

    closeButton.addEventListener('click', event => {
      event.preventDefault();
      event.stopPropagation();
      invalidateContext();
      options.onClose?.(panel);
    });

    mainButton.addEventListener('click', async event => {
      event.preventDefault();
      event.stopPropagation();

      if (dropdown.classList.contains('open')) {
        closeDropdown();
        return;
      }

      const requestRevision = ++contextRevision;

      let context;
      try {
        context = await contextProvider();
      } catch (error) {
        if (requestRevision !== contextRevision) return;
        showToast(panel, error?.message || PD.I18n.t('ytCannotAnalyze'), true);
        return;
      }

      if (requestRevision !== contextRevision) return;

      if (!context?.url) {
        showToast(panel, PD.I18n.t('ytCannotAnalyze'), true);
        return;
      }

      setLoading(true);
      let response = null;
      try {
        response = await analyze(context);
      } catch (_) { }

      if (requestRevision !== contextRevision) return;

      if (response?.success && Array.isArray(response.formats) && response.formats.length) {
        setLoading(false);
        openDropdown(response, context);
        return;
      }

      const fallbackResponse = await downloadDirectFallback(context);
      if (requestRevision !== contextRevision) return;
      setLoading(false);

      if (fallbackResponse?.success) {
        showToast(panel, PD.I18n.t('ytAddedToQueue'));
      } else {
        showToast(
          panel,
          fallbackResponse ? PD.I18n.t('ytDownloadError') : PD.I18n.t('ytCannotAnalyze'),
          true
        );
      }
    });

    return {
      element: panel,
      setContextProvider(provider) {
        invalidateContext();
        contextProvider = provider || (() => null);
      },
      setDropdownAlignment(alignment) {
        panel.classList.toggle('pd-quality-align-left', alignment === 'left');
      },
      invalidateContext,
      closeDropdown,
      showToast(message, error = false) { showToast(panel, message, error); }
    };
  }

  PD.QualityAnalyzer = {
    analyze,
    createPanel,
    sanitizeName,
    clearCache() { cache.clear(); }
  };
})(globalThis);
