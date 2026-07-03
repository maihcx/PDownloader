// ============================================================
// PD.I18n — lớp bọc mỏng quanh chrome.i18n.
//
// chrome.i18n.getMessage() tự động chọn locale khớp với ngôn ngữ UI của
// trình duyệt (chrome.i18n.getUILanguage()); nếu ngôn ngữ đó không có file
// trong _locales/, Chrome tự fallback về "default_locale" khai báo trong
// manifest.json. Vì vậy KHÔNG cần tự viết logic dò ngôn ngữ — chỉ cần cung
// cấp đủ file _locales/<lang>/messages.json là tự động "theo trình duyệt".
//
// File này được load trong CẢ 3 ngữ cảnh (background service worker qua
// importScripts, content script qua manifest content_scripts.js, và popup
// qua <script src>) — mỗi ngữ cảnh có global scope riêng nên mỗi nơi sẽ có
// 1 bản PD.I18n độc lập, đó là điều bình thường/mong đợi với mô hình
// namespace không cần bundler này.
// ============================================================
(function (root) {
  const PD = root.PD || (root.PD = {});

  function t(key, substitutions) {
    try {
      return chrome.i18n.getMessage(key, substitutions) || key;
    } catch (_) {
      return key;
    }
  }

  // Áp dụng bản dịch cho 1 cây DOM (mặc định toàn bộ document), dựa trên
  // các attribute quy ước:
  //   data-i18n             -> textContent
  //   data-i18n-placeholder -> placeholder
  //   data-i18n-title       -> title
  // Dùng trong popup.html để không phải hard-code chuỗi tiếng Việt/Anh
  // trong HTML.
  function applyToDom(root2) {
    const scope = root2 || document;

    scope.querySelectorAll('[data-i18n]').forEach(el => {
      el.textContent = t(el.getAttribute('data-i18n'));
    });
    scope.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
      el.setAttribute('placeholder', t(el.getAttribute('data-i18n-placeholder')));
    });
    scope.querySelectorAll('[data-i18n-title]').forEach(el => {
      el.setAttribute('title', t(el.getAttribute('data-i18n-title')));
    });
  }

  PD.I18n = { t, applyToDom };
})(typeof self !== 'undefined' ? self : this);
