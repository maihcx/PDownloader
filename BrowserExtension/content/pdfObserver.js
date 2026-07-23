(function () {
  if (window !== window.top) return;

  const PDF_EXTENSION = /\.pdf(?:[?#]|$)/i;
  const PDF_MIME = /^(?:application\/(?:pdf|x-pdf|acrobat|vnd\.pdf)|applications\/vnd\.pdf|text\/(?:pdf|x-pdf))(?:;|$)/i;
  const PDF_EMBED_SELECTOR = 'embed[src], object[data], iframe[src]';

  let currentCandidate = null;
  let bestPdfCandidate = null;
  let pdfButton = null;
  let hideTimer = null;
  let contextInvalidated = false;
  let lastPageUrl = location.href;
  const shownUrls = new Set();
  const registeredUrls = new Set();

  function sendMessage(message) {
    if (contextInvalidated || !PDWebExt.runtime?.id) return Promise.resolve(null);

    return PDWebExt.runtime.sendMessage(message).catch(error => {
      const text = String(error?.message || error || '');
      if (/extension context invalidated|receiving end does not exist|message port closed/i.test(text)) {
        contextInvalidated = true;
        hidePdfButton();
      }
      return null;
    });
  }

  function normalizeUrl(value) {
    try {
      const url = new URL(String(value || ''), location.href);
      if (!/^https?:$/i.test(url.protocol)) return '';
      url.hash = '';
      return url.href;
    } catch (_) {
      return '';
    }
  }

  function isPdfMime(value) {
    return PDF_MIME.test(String(value || '').trim());
  }

  function isPdfUrl(value) {
    return PDF_EXTENSION.test(String(value || ''));
  }

  function sanitizeName(value, fallback = 'document') {
    const name = String(value || fallback)
      .replace(/[\\/:*?"<>|]/g, '_')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/[. ]+$/g, '')
      .slice(0, 110);
    return name || fallback;
  }

  function filenameFromUrl(url) {
    try {
      const leaf = decodeURIComponent(new URL(url).pathname.split('/').pop() || '');
      if (/\.pdf$/i.test(leaf)) return leaf;
    } catch (_) { }
    return '';
  }

  function resolvePdfTarget(element) {
    if (!(element instanceof Element)) return null;

    const anchor = element.closest?.('a[href]');
    if (anchor) {
      const url = normalizeUrl(anchor.href || anchor.getAttribute('href'));
      if (!url) return null;

      const mime = anchor.getAttribute('type') || '';
      const downloadName = anchor.getAttribute('download') || '';
      if (!isPdfUrl(url) && !isPdfMime(mime) && !/\.pdf$/i.test(downloadName)) return null;

      return {
        url,
        mime,
        filename: /\.pdf$/i.test(downloadName) ? downloadName : filenameFromUrl(url),
        title: anchor.textContent?.trim() || document.title || 'PDF',
        source: 'user-click',
        requestType: 'link'
      };
    }

    return null;
  }

  function getEmbeddedPdfCandidate(element) {
    if (!(element instanceof Element)) return null;

    const tag = element.tagName?.toLowerCase();
    if (!['embed', 'object', 'iframe'].includes(tag)) return null;

    const rawUrl = tag === 'object'
      ? element.getAttribute('data')
      : element.getAttribute('src');
    const url = normalizeUrl(rawUrl);
    if (!url) return null;

    const mime = element.getAttribute('type') || '';
    if (!isPdfUrl(url) && !isPdfMime(mime)) return null;

    return {
      url,
      mime,
      filename: filenameFromUrl(url),
      title: element.getAttribute('title') || document.title || 'PDF',
      source: 'dom-pdf-visible',
      requestType: tag === 'object' ? 'object' : 'sub_frame'
    };
  }

  function isVisible(element) {
    if (!(element instanceof Element) || !element.isConnected) return false;
    const rect = element.getBoundingClientRect();
    if (rect.width < 80 || rect.height < 80) return false;
    const style = getComputedStyle(element);
    return style.display !== 'none'
      && style.visibility !== 'hidden'
      && Number.parseFloat(style.opacity || '1') > 0.01;
  }

  function hasVisibleEmbeddedPdf(candidateUrl = '') {
    const wanted = normalizeUrl(candidateUrl);

    for (const element of document.querySelectorAll(PDF_EMBED_SELECTOR)) {
      if (!isVisible(element)) continue;
      const candidate = getEmbeddedPdfCandidate(element);
      if (!candidate) continue;
      if (!wanted || normalizeUrl(candidate.url) === wanted) return true;
    }

    return false;
  }

  function createCandidate(raw, overrides = {}) {
    const url = normalizeUrl(raw?.url || raw);
    if (!url) return null;

    return {
      url,
      mediaType: 'pdf',
      kind: 'direct',
      mime: raw?.mime || overrides.mime || '',
      extension: 'pdf',
      filename: raw?.filename || overrides.filename || filenameFromUrl(url),
      title: raw?.title || overrides.title || document.title || 'PDF',
      pageUrl: location.href,
      referer: location.href,
      source: raw?.source || overrides.source || 'dom-pdf',
      requestType: raw?.requestType || overrides.requestType || '',
      score: Number.isFinite(raw?.score) ? raw.score : 180
    };
  }

  function updateButtonOffset() {
    if (!pdfButton) return;
    const audioVisible = !!document.querySelector('.pd-audio-grab-btn.pd-visible');
    pdfButton.style.bottom = audioVisible ? '64px' : '18px';
  }

  function clearHideTimer() {
    if (!hideTimer) return;
    clearTimeout(hideTimer);
    hideTimer = null;
  }

  function scheduleHide(delay = 12000) {
    clearHideTimer();
    hideTimer = setTimeout(() => {
      hideTimer = null;
      hidePdfButton();
    }, delay);
  }

  function ensurePdfButton() {
    if (pdfButton) return pdfButton;

    const style = document.createElement('style');
    style.textContent = `
      .pd-pdf-grab-btn {
        position: fixed;
        right: 18px;
        bottom: 18px;
        z-index: 2147483647;
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px 12px;
        border-radius: 9px;
        border: 1px solid var(--pd-border, rgba(100,100,100,.3));
        background: var(--pd-bg, rgba(30,30,30,.92));
        color: var(--pd-text, #fff);
        box-shadow: 0 6px 24px rgba(0,0,0,.2);
        backdrop-filter: blur(14px);
        -webkit-backdrop-filter: blur(14px);
        font: 600 12px/1.2 'Segoe UI', system-ui, sans-serif;
        cursor: pointer;
        opacity: 0;
        visibility: hidden;
        transform: translateY(6px);
        transition: opacity .18s, transform .18s, border-color .15s, bottom .18s;
      }
      .pd-pdf-grab-btn:hover {
        border-color: var(--pd-accent, #4fc3f7);
      }
      .pd-pdf-grab-btn.pd-visible {
        opacity: 1;
        visibility: visible;
        transform: translateY(0);
      }
      .pd-pdf-grab-btn.pd-success {
        border-color: var(--pd-green, #4caf50);
      }
      .pd-pdf-grab-icon {
        min-width: 25px;
        height: 17px;
        padding: 0 4px;
        border-radius: 4px;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        background: var(--pd-accent-bg, rgba(79,195,247,.14));
        color: var(--pd-accent, #4fc3f7);
        font: 700 9px/1 'Segoe UI', system-ui, sans-serif;
        letter-spacing: .2px;
      }
    `;
    document.documentElement.appendChild(style);

    pdfButton = document.createElement('button');
    pdfButton.type = 'button';
    pdfButton.className = 'pd-pdf-grab-btn pd-theme-root';

    const icon = document.createElement('span');
    icon.className = 'pd-pdf-grab-icon';
    icon.textContent = 'PDF';

    const label = document.createElement('span');
    label.className = 'pd-pdf-grab-label';
    label.textContent = PD.I18n.t('contentDownloadPdf');

    pdfButton.append(icon, label);

    pdfButton.addEventListener('pointerenter', clearHideTimer);
    pdfButton.addEventListener('pointerleave', () => scheduleHide(3500));

    pdfButton.addEventListener('click', async event => {
      event.preventDefault();
      event.stopPropagation();

      const candidate = currentCandidate || bestPdfCandidate;
      if (!candidate?.url) return;

      clearHideTimer();
      const labelElement = pdfButton.querySelector('.pd-pdf-grab-label');
      labelElement.textContent = PD.I18n.t('contentAddingPdf');

      const response = await sendMessage({
        action: 'download_media_candidate',
        candidateId: candidate.id || '',
        candidate,
        mediaType: 'pdf'
      });

      const ok = !!response?.success;
      labelElement.textContent = ok
        ? PD.I18n.t('ytAdded')
        : `✗ ${response?.error || PD.I18n.t('genericError')}`;
      pdfButton.classList.toggle('pd-success', ok);

      setTimeout(() => {
        if (!pdfButton?.isConnected) return;
        labelElement.textContent = PD.I18n.t('contentDownloadPdf');
        pdfButton.classList.remove('pd-success');
        if (ok) hidePdfButton();
        else scheduleHide(5000);
      }, 2200);
    });

    (document.body || document.documentElement).appendChild(pdfButton);
    updateButtonOffset();
    return pdfButton;
  }

  function hidePdfButton() {
    clearHideTimer();
    if (!pdfButton) return;
    pdfButton.classList.remove('pd-visible');
  }

  function showPdfButton(candidate, { markShown = true } = {}) {
    const normalized = normalizeUrl(candidate?.url);
    if (!normalized) return false;
    if (markShown && shownUrls.has(normalized)) return false;

    currentCandidate = candidate;
    if (markShown) shownUrls.add(normalized);

    const button = ensurePdfButton();
    updateButtonOffset();
    button.querySelector('.pd-pdf-grab-label').textContent = PD.I18n.t('contentDownloadPdf');
    button.classList.remove('pd-success');
    button.classList.add('pd-visible');
    scheduleHide();
    return true;
  }

  async function registerPdfCandidate(rawCandidate, show = false) {
    const candidate = createCandidate(rawCandidate);
    if (!candidate) return null;

    const normalized = normalizeUrl(candidate.url);
    let registered = candidate;

    if (!registeredUrls.has(normalized)) {
      registeredUrls.add(normalized);
      const response = await sendMessage({
        action: 'register_media_candidate',
        candidate
      });
      registered = response?.candidate || candidate;
    }

    if (show) showPdfButton(registered);
    return registered;
  }

  function isDirectPdfPage(candidate) {
    if (!candidate?.url) return false;
    const sameUrl = normalizeUrl(candidate.url) === normalizeUrl(location.href);
    return sameUrl && (
      candidate.requestType === 'main_frame'
      || isPdfUrl(location.href)
      || isPdfMime(document.contentType)
    );
  }

  function maybeShowNetworkCandidate(candidate) {
    if (!candidate?.url) return;

    if (isDirectPdfPage(candidate)) {
      showPdfButton(candidate);
      return;
    }

    if (['sub_frame', 'object'].includes(candidate.requestType)
        && hasVisibleEmbeddedPdf(candidate.url)) {
      showPdfButton(candidate);
    }
  }

  function scanEmbeddedPdfs(root = document) {
    const elements = [];
    if (root instanceof Element && root.matches?.(PDF_EMBED_SELECTOR)) elements.push(root);
    for (const element of root.querySelectorAll?.(PDF_EMBED_SELECTOR) || []) elements.push(element);

    for (const element of elements) {
      if (!isVisible(element)) continue;
      const candidate = getEmbeddedPdfCandidate(element);
      if (!candidate) continue;
      void registerPdfCandidate(candidate, true);
    }
  }

  function detectDirectPdfPage() {
    if (!isPdfUrl(location.href) && !isPdfMime(document.contentType)) return;

    void registerPdfCandidate({
      url: location.href,
      mime: document.contentType || 'application/pdf',
      filename: filenameFromUrl(location.href),
      title: document.title || filenameFromUrl(location.href) || 'PDF',
      source: 'direct-pdf-page',
      requestType: 'main_frame',
      score: 240
    }, true);
  }

  function handlePageChange() {
    if (location.href === lastPageUrl) return;
    lastPageUrl = location.href;
    currentCandidate = null;
    bestPdfCandidate = null;
    shownUrls.clear();
    registeredUrls.clear();
    hidePdfButton();

    setTimeout(() => {
      detectDirectPdfPage();
      scanEmbeddedPdfs();
    }, 250);
  }

  document.addEventListener('pointerdown', event => {
    if (event.target instanceof Element && event.target.closest('.pd-pdf-grab-btn')) return;
    const target = resolvePdfTarget(event.target);
    if (!target) return;
    void registerPdfCandidate(target, true);
  }, true);

  document.addEventListener('auxclick', event => {
    const target = resolvePdfTarget(event.target);
    if (!target) return;
    void registerPdfCandidate(target, true);
  }, true);

  PDWebExt.runtime.onMessage.addListener(message => {
    if (message?.action !== 'media_candidates_updated') return false;

    bestPdfCandidate = message.bestPdf || null;
    updateButtonOffset();
    maybeShowNetworkCandidate(bestPdfCandidate);
    return false;
  });

  const observer = new MutationObserver(records => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node instanceof Element) scanEmbeddedPdfs(node);
      }
    }
    updateButtonOffset();
  });

  observer.observe(document.documentElement, { childList: true, subtree: true });

  detectDirectPdfPage();
  scanEmbeddedPdfs();

  setInterval(() => {
    handlePageChange();
    updateButtonOffset();
  }, 1500);
})();
