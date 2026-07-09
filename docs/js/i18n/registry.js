import { en } from "./en.js";
import { vi } from "./vi.js";

// Để thêm ngôn ngữ mới:
// 1. Tạo file js/i18n/xx.js, copy cấu trúc key giống en.js, dịch giá trị.
// 2. Import và thêm vào LOCALES bên dưới.
// 3. Thêm "xx" vào supportedLangs trong js/config.js.
// 4. Thêm một nút bấm cho "xx" trong .lang-switch trong index.html.
export const LOCALES = {
  en,
  vi,
};

export const LOCALE_LABELS = {
  en: "EN",
  vi: "VI",
};
