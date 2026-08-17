(function () {
  if (window !== window.top || !PD.QualityAnalyzer) return;

  let currentVideoId = '';

  function getVideoId() {
    if (location.pathname.startsWith('/shorts/')) return location.pathname.split('/')[2] || '';
    return new URLSearchParams(location.search).get('v') || '';
  }

  function isShorts() {
    return location.pathname.startsWith('/shorts/');
  }

  function getVideoUrl(videoId) {
    return isShorts()
      ? `https://www.youtube.com/shorts/${videoId}`
      : `https://www.youtube.com/watch?v=${videoId}`;
  }

  function getTitle(videoId) {
    const title = String(document.title || '').replace(/\s*-\s*YouTube\s*$/i, '').trim();
    return title || `YouTube_${videoId}`;
  }

  function removePanel() {
    document.querySelectorAll('.pd-quality-panel.pd-youtube-quality').forEach(panel => panel.remove());
  }

  function injectPanel() {
    const videoId = getVideoId();
    if (!videoId) {
      currentVideoId = '';
      removePanel();
      return;
    }

    if (videoId !== currentVideoId) {
      currentVideoId = videoId;
      removePanel();
      void PD.QualityAnalyzer.analyze({
        url: getVideoUrl(videoId),
        cacheKey: `youtube:${videoId}`,
        title: getTitle(videoId),
        referer: location.href
      });
    }

    const shorts = isShorts();
    const player = document.querySelector('#movie_player')
      || document.querySelector('.html5-video-player')
      || (shorts ? document.querySelector('ytd-reel-video-renderer[is-active] #player') : null)
      || (shorts ? document.querySelector('#shorts-player') : null);

    if (!player || player.querySelector('.pd-youtube-quality')) return;

    const controller = PD.QualityAnalyzer.createPanel({
      className: `pd-youtube-quality${shorts ? ' pd-quality-shorts' : ''}`,
      getContext: () => ({
        url: getVideoUrl(currentVideoId),
        cacheKey: `youtube:${currentVideoId}`,
        title: getTitle(currentVideoId),
        referer: location.href
      }),
      onClose: panel => {
        panel.style.display = 'none';
      }
    });

    player.appendChild(controller.element);
  }

  let scheduled = false;
  function scheduleInject() {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      injectPanel();
    });
  }

  document.addEventListener('yt-navigate-finish', scheduleInject);
  const observer = new MutationObserver(scheduleInject);
  observer.observe(document.documentElement, { childList: true, subtree: true });

  setTimeout(injectPanel, 500);
  injectPanel();
})();
