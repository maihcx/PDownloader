import { setLanguage, getLanguage, onLanguageChange } from "../i18n/i18n-core.js";

export function initLangSwitcher() {
  const buttons = document.querySelectorAll("[data-lang-btn]");

  const syncPressedState = (lang) => {
    buttons.forEach((btn) => {
      btn.setAttribute("aria-pressed", String(btn.dataset.langBtn === lang));
    });
  };

  buttons.forEach((btn) => {
    btn.addEventListener("click", () => setLanguage(btn.dataset.langBtn));
  });

  onLanguageChange(syncPressedState);
  syncPressedState(getLanguage());
}
