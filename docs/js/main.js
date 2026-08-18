import { initI18n } from "./i18n/i18n-core.js";
import { initNavbar } from "./modules/navbar.js";
import { initLangSwitcher } from "./modules/lang-switcher.js";
import { initRevealOnScroll } from "./modules/reveal-on-scroll.js";
import { initRunnerPreview } from "./modules/runner-preview.js";
import { initCopySnippet } from "./modules/copy-snippet.js";
import { applyConfigLinks } from "./modules/apply-config-links.js";
import { initBrowserExtensionDialog } from "./modules/browser-extension-dialog.js";
import { initThemeSwitcher } from "./modules/theme-switcher.js";

document.addEventListener("DOMContentLoaded", () => {
  initI18n();
  applyConfigLinks();
  initNavbar();
  initLangSwitcher();
  initRevealOnScroll();
  initRunnerPreview();
  initCopySnippet();
  initBrowserExtensionDialog();
  initThemeSwitcher();
});
