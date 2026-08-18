import { onLanguageChange, t } from "../i18n/i18n-core.js";

const STATES = ["downloading", "merging", "completed"];
const STATE_DURATION = {
  downloading: 5200,
  merging: 2800,
  completed: 3800,
};

const DOWNLOAD_STREAMS = [
  { progress: 49, amount: "348.7 MB / 705.3 MB", speed: "5.2 MB/s" },
  { progress: 100, amount: "705.3 MB / 705.3 MB", speed: "–", tone: "complete" },
  { progress: 79, amount: "557.1 MB / 705.3 MB", speed: "9.8 MB/s" },
  { progress: 36, amount: "251.3 MB / 705.3 MB", speed: "2.5 MB/s" },
  { progress: 32, amount: "227.0 MB / 705.3 MB", speed: "1.2 MB/s" },
  { progress: 64, amount: "451.9 MB / 705.3 MB", speed: "32.0 MB/s" },
  { progress: 35, amount: "249.3 MB / 705.3 MB", speed: "5.4 MB/s" },
  { progress: 54, amount: "382.6 MB / 705.3 MB", speed: "9.0 MB/s" },
  { progress: 100, amount: "705.3 MB / 705.3 MB", speed: "–", tone: "complete" },
];

const FULL_STREAMS = Array.from({ length: 9 }, () => ({
  progress: 100,
  amount: "705.3 MB / 705.3 MB",
  speed: "–",
}));

const STATE_DATA = {
  downloading: {
    progress: 61,
    size: "3.79 GB / 6.20 GB",
    speed: "↓ 65.2 MB/s",
    eta: "ETA: 00:37",
    streams: DOWNLOAD_STREAMS,
  },
  merging: {
    progress: 15,
    size: "6.20 GB / 6.20 GB",
    speed: "↓ –",
    eta: "ETA: –",
    streams: FULL_STREAMS,
  },
  completed: {
    progress: 100,
    size: "6.20 GB / 6.20 GB",
    speed: "↓ –",
    eta: "ETA: –",
    streams: FULL_STREAMS.map((stream) => ({ ...stream, tone: "complete" })),
  },
};

function setText(root, name, value) {
  const element = root.querySelector(`[data-runner-text="${name}"]`);
  if (element) element.textContent = value;
}

function threadLabel(number) {
  return t("hero.runner.thread").replace("{number}", number);
}

function createStream(stream, index) {
  const item = document.createElement("div");
  item.className = "runner-stream";
  item.dataset.tone = stream.tone || "progress";
  item.style.setProperty("--stream-progress", `${stream.progress}%`);

  const top = document.createElement("div");
  top.className = "runner-stream__top";

  const label = document.createElement("span");
  label.textContent = threadLabel(index + 1);
  const speed = document.createElement("span");
  speed.textContent = stream.speed;
  top.append(label, speed);

  const bottom = document.createElement("div");
  bottom.className = "runner-stream__bottom";

  const amount = document.createElement("span");
  amount.textContent = stream.amount;
  const progress = document.createElement("span");
  progress.textContent = `${stream.progress}%`;
  bottom.append(amount, progress);

  item.append(top, bottom);
  return item;
}

export function initRunnerPreview() {
  const root = document.querySelector("[data-runner-preview]");
  if (!root) return;

  const progressBar = root.querySelector("[data-runner-progress]");
  const streamsGrid = root.querySelector("[data-runner-streams]");
  const completedStatus = root.querySelector("[data-runner-complete-status]");
  const pauseButton = root.querySelector('[data-runner-action="pause"]');
  const cancelButton = root.querySelector('[data-runner-action="cancel"]');
  const folderButton = root.querySelector('[data-runner-action="folder"]');
  const openButton = root.querySelector('[data-runner-action="open"]');
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  let currentIndex = 0;
  let timerId = null;

  function render() {
    const state = STATES[currentIndex];
    const data = STATE_DATA[state];
    const isCompleted = state === "completed";

    root.dataset.state = state;
    setText(root, "title", t("hero.runner.title"));
    setText(root, "state", t(`hero.runner.${state}`));
    setText(root, "percent", `${data.progress}%`);
    setText(root, "size", data.size);
    setText(root, "speed", data.speed);
    setText(root, "eta", data.eta);
    setText(root, "streamsTitle", t("hero.runner.streamsTitle"));
    setText(root, "pause", t("hero.runner.pause"));
    setText(root, "cancel", t("hero.runner.cancel"));
    setText(root, "done", t("hero.runner.done"));
    setText(root, "openFolder", t("hero.runner.openFolder"));
    setText(root, "openFile", t("hero.runner.openFile"));

    if (progressBar) progressBar.style.width = `${data.progress}%`;
    if (streamsGrid) streamsGrid.replaceChildren(...data.streams.map(createStream));

    if (completedStatus) completedStatus.hidden = !isCompleted;
    if (pauseButton) pauseButton.hidden = isCompleted;
    if (cancelButton) {
      cancelButton.hidden = isCompleted;
      cancelButton.disabled = state === "merging";
    }
    if (folderButton) folderButton.hidden = !isCompleted;
    if (openButton) openButton.hidden = !isCompleted;
  }

  function scheduleNextState() {
    if (reduceMotion) return;
    window.clearTimeout(timerId);
    timerId = window.setTimeout(() => {
      currentIndex = (currentIndex + 1) % STATES.length;
      render();
      scheduleNextState();
    }, STATE_DURATION[STATES[currentIndex]]);
  }

  render();
  scheduleNextState();
  onLanguageChange(render);
}
