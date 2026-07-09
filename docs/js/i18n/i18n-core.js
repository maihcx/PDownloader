import { LOCALES } from "./registry.js";
import { SITE_CONFIG } from "../config.js";

const STORAGE_KEY = "pdownloader-lang";
let currentLang = null;
const listeners = [];

function resolveKey(dict, path) {
  return path.split(".").reduce((node, part) => (node && node[part] !== undefined ? node[part] : undefined), dict);
}

function detectInitialLang() {
  const saved = localStorage.getItem(STORAGE_KEY);
  if (saved && LOCALES[saved]) return saved;

  const browserLang = (navigator.language || "").slice(0, 2);
  if (SITE_CONFIG.supportedLangs.includes(browserLang)) return browserLang;

  return SITE_CONFIG.defaultLang;
}

export function t(key) {
  const value = resolveKey(LOCALES[currentLang], key);
  if (value === undefined) {
    console.warn(`[i18n] Missing key "${key}" for locale "${currentLang}"`);
    return key;
  }
  return value;
}

function applyToDOM() {
  document.documentElement.setAttribute("lang", currentLang);

  document.querySelectorAll("[data-i18n]").forEach((el) => {
    el.textContent = t(el.getAttribute("data-i18n"));
  });

  document.querySelectorAll("[data-i18n-attr]").forEach((el) => {
    // format: "placeholder:key.path,title:key.path2"
    el.getAttribute("data-i18n-attr")
      .split(",")
      .map((pair) => pair.trim().split(":"))
      .forEach(([attr, key]) => {
        el.setAttribute(attr, t(key));
      });
  });

  const meta = LOCALES[currentLang].meta;
  if (meta) {
    document.title = meta.title;
    const desc = document.querySelector('meta[name="description"]');
    if (desc) desc.setAttribute("content", meta.description);
  }
}

export function setLanguage(lang) {
  if (!LOCALES[lang]) return;
  currentLang = lang;
  localStorage.setItem(STORAGE_KEY, lang);
  applyToDOM();
  listeners.forEach((fn) => fn(lang));
}

export function getLanguage() {
  return currentLang;
}

export function onLanguageChange(fn) {
  listeners.push(fn);
}

export function initI18n() {
  setLanguage(detectInitialLang());
}
