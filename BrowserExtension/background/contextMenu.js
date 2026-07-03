// ============================================================
// PD.ContextMenu — menu chuột phải "Tải ... với PDownloader".
// ============================================================
(function (root) {
  const PD = root.PD || (root.PD = {});

  function createMenus() {
    chrome.contextMenus.removeAll(() => {
      chrome.contextMenus.create({ id: 'pd-link',      title: PD.I18n.t('contextMenuLink'),  contexts: ['link'] });
      chrome.contextMenus.create({ id: 'pd-image',     title: PD.I18n.t('contextMenuImage'), contexts: ['image'] });
      chrome.contextMenus.create({ id: 'pd-media',     title: PD.I18n.t('contextMenuMedia'), contexts: ['video','audio'] });
      chrome.contextMenus.create({ id: 'pd-separator', type: 'separator',                    contexts: ['link','image','video','audio','page'] });
      chrome.contextMenus.create({ id: 'pd-page',      title: PD.I18n.t('contextMenuPage'),  contexts: ['page'] });
    });
  }

  // QUAN TRỌNG: onClicked phải được đăng ký ĐỒNG BỘ ở top-level mỗi lần
  // service worker khởi động lại (không đặt trong nhánh chỉ chạy lúc
  // onInstalled/onStartup) — đây là yêu cầu bắt buộc của MV3 để Chrome có
  // thể "đánh thức" lại service worker khi người dùng click menu, kể cả sau
  // khi nó đã bị idle-unload. Việc TẠO menu (createMenus, có side-effect
  // removeAll+create) thì vẫn chỉ cần chạy lúc cài đặt/khởi động trình
  // duyệt như bản gốc.
  function init() {
    chrome.contextMenus.onClicked.addListener(handleClick);
  }

  async function handleClick(info, tab) {
    let url = '';
    if      (info.menuItemId === 'pd-link')  url = info.linkUrl  || '';
    else if (info.menuItemId === 'pd-image') url = info.srcUrl   || '';
    else if (info.menuItemId === 'pd-media') url = info.srcUrl   || info.linkUrl || '';
    else if (info.menuItemId === 'pd-page')  url = info.pageUrl  || tab?.url || '';
    if (!url) return;

    const filename = PD.Utils.getFilenameFromUrl(url);
    const referer   = tab?.url || '';
    const ok = await PD.Api.sendDownload(url, filename, referer);
    if (ok) {
      PD.State.incrementInterceptCount();
      PD.Badge.update();
      await PD.Notify.show(filename || url);
    }
  }

  PD.ContextMenu = { init, createMenus };
})(self);
