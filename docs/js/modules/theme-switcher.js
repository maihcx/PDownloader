import { t, onLanguageChange } from "../i18n/i18n-core.js";

const STORAGE_KEY = "pdownloader-theme";

function getSavedTheme() {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function saveTheme(theme) {
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // The selected theme still applies for the current page view.
  }
}

export function initThemeSwitcher() {
  const button = document.querySelector("[data-theme-toggle]");
  if (!button) return;

  const colorScheme = window.matchMedia("(prefers-color-scheme: dark)");

  const applyTheme = (theme, source) => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.themeSource = source;
    syncButton();
  };

  const syncButton = () => {
    const currentTheme = document.documentElement.dataset.theme || "light";
    const label = t(currentTheme === "dark" ? "theme.toLight" : "theme.toDark");
    button.setAttribute("aria-label", label);
    button.setAttribute("title", label);
    button.setAttribute("aria-pressed", String(currentTheme === "dark"));
  };

  button.addEventListener("click", () => {
    const currentTheme = document.documentElement.dataset.theme || "light";
    const nextTheme = currentTheme === "dark" ? "light" : "dark";
    saveTheme(nextTheme);
    applyTheme(nextTheme, "saved");
  });

  colorScheme.addEventListener("change", (event) => {
    if (getSavedTheme()) return;
    applyTheme(event.matches ? "dark" : "light", "system");
  });

  onLanguageChange(syncButton);
  syncButton();
}
