import { initI18n } from "./i18n/i18n-core.js";
import { initNavbar } from "./modules/navbar.js";
import { initLangSwitcher } from "./modules/lang-switcher.js";
import { initRevealOnScroll } from "./modules/reveal-on-scroll.js";
import { initSegmentBar } from "./modules/segment-bar.js";
import { initCopySnippet } from "./modules/copy-snippet.js";
import { applyConfigLinks } from "./modules/apply-config-links.js";

document.addEventListener("DOMContentLoaded", () => {
  initI18n();
  applyConfigLinks();
  initNavbar();
  initLangSwitcher();
  initRevealOnScroll();
  initSegmentBar();
  initCopySnippet();
});
