// ============================================================
// PD.MessageRouter — 1 điểm vào duy nhất xử lý mọi message từ popup và
// content script (trước đây tách thành 2 listener riêng trong background.js
// gốc, gộp lại đây cho rõ luồng).
// ============================================================
(function (root) {
  const PD = root.PD || (root.PD = {});
  const { Api, Badge, Notify, State, Storage, HlsCapture } = PD;

  const handlers = {
    ping_app(_msg, _sender, sendResponse) {
      Api.ping().then(ok => sendResponse({ connected: ok }));
      return true;
    },

    download(msg, _sender, sendResponse) {
      Api.sendDownload(msg.url, msg.filename || null, msg.referer || '').then(ok => {
        if (ok) {
          State.incrementInterceptCount();
          Badge.update();
          Notify.show(msg.filename || msg.url);
        }
        sendResponse({ success: ok });
      });
      return true;
    },

    download_magnet(msg) {
      Api.sendDownload(msg.url, null, '').then(() => {});
      return false;
    },

    get_intercept_count(_msg, _sender, sendResponse) {
      sendResponse({ count: State.getInterceptCount() });
      return false;
    },

    reset_badge(_msg, _sender, sendResponse) {
      State.resetInterceptCount();
      Badge.update();
      sendResponse({ success: true });
      return false;
    },

    get_settings(_msg, _sender, sendResponse) {
      Storage.getSettings().then(sendResponse);
      return true;
    },

    // Gộp 3 lệnh (ping app, đếm intercept, lấy settings) thành 1 round-trip
    // message duy nhất cho popup lúc mở lên — tránh phải đánh thức service
    // worker MV3 (có thể đang "ngủ") nhiều lần liên tiếp.
    get_popup_init(_msg, _sender, sendResponse) {
      Promise.all([Api.ping(), Storage.getSettings()]).then(([connected, settings]) => {
        sendResponse({ connected, interceptCount: State.getInterceptCount(), settings });
      });
      return true;
    },

    save_settings(msg, _sender, sendResponse) {
      Storage.saveSettings(msg.settings).then(() => sendResponse({ success: true }));
      return true;
    },

    add_blacklist(msg, _sender, sendResponse) {
      Storage.addBlacklist(msg.domain).then(() => sendResponse({ success: true }));
      return true;
    },

    remove_blacklist(msg, _sender, sendResponse) {
      Storage.removeBlacklist(msg.domain).then(() => sendResponse({ success: true }));
      return true;
    },

    get_hls_manifest(_msg, sender, sendResponse) {
      const found = HlsCapture.get(sender.tab?.id);
      sendResponse(found ? { url: found.url, referer: found.referer } : null);
      return true;
    },

    // Dùng lại pipeline yt-dlp (endpoint /youtube/*), nhưng thực ra hoàn toàn
    // tổng quát — yt-dlp tự hỗ trợ Facebook/TikTok/Instagram/Twitter/Vimeo/
    // Twitch/Reddit/Bilibili/SoundCloud, và cả raw .m3u8/.mpd URL bắt được
    // qua webRequest. Nút "Tải video này" là one-click nên không hỏi chất
    // lượng, luôn lấy "bestvideo+bestaudio/best".
    download_via_ytdlp(msg, _sender, sendResponse) {
      Api.ytDownload({
        url: msg.url, formatId: 'bestvideo+bestaudio/best',
        filename: msg.filename, title: msg.title || msg.filename,
        filesize: 0, referer: msg.referer
      }).then(result => {
        if (result.success) {
          State.incrementInterceptCount();
          Badge.update();
          Notify.show(msg.filename || msg.title);
        }
        sendResponse(result);
      });
      return true;
    },

    analyze_youtube(msg, _sender, sendResponse) {
      Api.ytAnalyze(msg.url).then(sendResponse);
      return true;
    },

    download_youtube(msg, _sender, sendResponse) {
      Api.ytDownload({
        url: msg.url, formatId: msg.formatId, filename: msg.filename,
        title: msg.title, filesize: msg.filesize || 0
      }).then(sendResponse);
      return true;
    }
  };

  function init() {
    chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
      const handler = handlers[msg?.action];
      if (!handler) return false;
      return handler(msg, sender, sendResponse);
    });
  }

  PD.MessageRouter = { init };
})(self);
