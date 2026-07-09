# PDownloader — Trang chủ tĩnh (GitHub Pages)

Trang HTML/CSS/JS thuần, không cần build, dùng ES module trực tiếp.

## Cách dùng nhanh

1. Copy nguyên thư mục này vào repo (ví dụ đặt ở nhánh `gh-pages` hoặc thư mục `/docs`).
2. Sửa các URL trong `js/config.js` (link GitHub, link release, link license).
3. Bật GitHub Pages trỏ vào thư mục chứa `index.html`.

Vì dùng `<script type="module">`, trang **phải chạy qua HTTP** (GitHub Pages OK), không mở trực tiếp bằng `file://`. Để xem thử ở máy local:

```bash
python3 -m http.server 8000
# rồi mở http://localhost:8000
```

## Cấu trúc

```
index.html            → khung trang, chỉ chứa markup + data-i18n attributes
css/01-14...css        → mỗi file phụ trách một phần (tokens, navbar, hero, ...)
js/config.js            → nơi duy nhất cần sửa link GitHub/release
js/main.js              → khởi tạo các module
js/i18n/registry.js      → đăng ký ngôn ngữ — thêm ngôn ngữ mới tại đây
js/i18n/en.js, vi.js     → nội dung dịch, cùng cấu trúc key
js/i18n/i18n-core.js     → engine áp dụng bản dịch vào DOM (không đổi khi thêm ngôn ngữ)
js/modules/*.js          → mỗi hiệu ứng/tính năng UI một file riêng
```

## Thêm ngôn ngữ mới (ví dụ tiếng Nhật)

1. Tạo `js/i18n/ja.js`, copy cấu trúc từ `en.js`, dịch từng giá trị.
2. Trong `js/i18n/registry.js`: import và thêm `ja` vào `LOCALES`, thêm nhãn vào `LOCALE_LABELS`.
3. Trong `js/config.js`: thêm `"ja"` vào `supportedLangs`.
4. Trong `index.html`, thêm một nút trong `.lang-switch`:
   ```html
   <button type="button" data-lang-btn="ja">JA</button>
   ```

Không cần đụng vào `i18n-core.js` hay bất kỳ file CSS/JS nào khác.

## Ghi chú

- Toàn bộ nội dung động dùng `data-i18n="key.path"` (đổi `textContent`) hoặc `data-i18n-attr="attr:key.path"` (đổi thuộc tính).
- Lựa chọn ngôn ngữ được lưu vào `localStorage`, lần sau vào lại trang sẽ nhớ.
- Hiệu ứng thanh tải phân đoạn ở hero tôn trọng `prefers-reduced-motion`.
