export function initCopySnippet() {
  document.querySelectorAll("[data-copy-target]").forEach((btn) => {
    const targetEl = document.querySelector(btn.getAttribute("data-copy-target"));
    if (!targetEl) return;

    const originalLabel = btn.querySelector("span").textContent;

    btn.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(targetEl.textContent.trim());
        btn.querySelector("span").textContent = "copied";
        window.setTimeout(() => {
          btn.querySelector("span").textContent = originalLabel;
        }, 1600);
      } catch (err) {
        console.warn("Clipboard copy failed:", err);
      }
    });
  });
}
