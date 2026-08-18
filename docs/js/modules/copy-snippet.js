import { t, onLanguageChange } from "../i18n/i18n-core.js";

export function initCopySnippet() {
  document.querySelectorAll("[data-copy-target]").forEach((btn) => {
    const targetEl = document.querySelector(btn.getAttribute("data-copy-target"));
    const label = btn.querySelector("span");
    if (!targetEl || !label) return;

    let state = "idle";
    let resetTimer = null;

    const syncLabel = () => {
      const key = state === "success" ? "cta.copied" : state === "error" ? "cta.copyError" : "cta.copy";
      const text = t(key);
      label.textContent = text;
      btn.setAttribute("aria-label", text);
    };

    const scheduleReset = () => {
      window.clearTimeout(resetTimer);
      resetTimer = window.setTimeout(() => {
        state = "idle";
        syncLabel();
      }, 1800);
    };

    btn.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(targetEl.textContent.trim());
        state = "success";
      } catch (err) {
        state = "error";
        console.warn("Clipboard copy failed:", err);
      }
      syncLabel();
      scheduleReset();
    });

    onLanguageChange(syncLabel);
    syncLabel();
  });
}
