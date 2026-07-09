import { t, onLanguageChange } from "../i18n/i18n-core.js";

const SEGMENT_COUNT = 8;

function rand(min, max) {
  return Math.round(min + Math.random() * (max - min));
}

export function initSegmentBar() {
  const track = document.querySelector("[data-segbar-track]");
  const status = document.querySelector("[data-segbar-status]");
  if (!track) return;

  const prefersReduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  const segments = Array.from({ length: SEGMENT_COUNT }, (_, i) => {
    const seg = document.createElement("div");
    seg.className = "seg";
    seg.innerHTML = '<div class="seg-fill"></div>';
    track.appendChild(seg);
    return seg;
  });

  const updateStatusText = () => {
    if (!status) return;
    const state = status.dataset.state;
    status.textContent = state === "merged" ? t("segbar.statusMerged") : t("segbar.statusRunning");
  };
  onLanguageChange(updateStatusText);

  if (prefersReduced) {
    segments.forEach((seg) => seg.classList.add("is-done"));
    track.classList.add("is-merged");
    if (status) status.dataset.state = "merged";
    updateStatusText();
    return;
  }

  async function runCycle() {
    // Reset
    track.classList.remove("is-merged");
    if (status) status.dataset.state = "running";
    updateStatusText();

    segments.forEach((seg) => {
      seg.classList.remove("is-done");
      const fill = seg.querySelector(".seg-fill");
      fill.style.transition = "none";
      fill.style.width = "0%";
      // force reflow so the next transition re-triggers
      void fill.offsetWidth;
    });

    let maxTime = 0;
    segments.forEach((seg, i) => {
      const duration = rand(900, 2000);
      const delay = i * 70 + rand(0, 120);
      maxTime = Math.max(maxTime, duration + delay);

      const fill = seg.querySelector(".seg-fill");
      fill.style.transition = `width ${duration}ms cubic-bezier(0.16,1,0.3,1) ${delay}ms`;
      requestAnimationFrame(() => {
        fill.style.width = "100%";
      });

      window.setTimeout(() => seg.classList.add("is-done"), delay + duration);
    });

    await new Promise((resolve) => window.setTimeout(resolve, maxTime + 150));

    track.classList.add("is-merged");
    if (status) status.dataset.state = "merged";
    updateStatusText();

    await new Promise((resolve) => window.setTimeout(resolve, 2600));
    runCycle();
  }

  runCycle();
}
