import { SITE_CONFIG } from "../config.js";

export function applyConfigLinks() {
  document.querySelectorAll("[data-href-config]").forEach((el) => {
    const key = el.getAttribute("data-href-config");
    if (SITE_CONFIG[key]) el.setAttribute("href", SITE_CONFIG[key]);
  });
}
