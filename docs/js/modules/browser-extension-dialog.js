export function initBrowserExtensionDialog() {
  const dialog = document.querySelector("#browser-extension-dialog");
  if (!(dialog instanceof HTMLDialogElement)) return;

  const openButtons = document.querySelectorAll("[data-extension-dialog-open]");
  const closeButton = dialog.querySelector("[data-extension-dialog-close]");
  const chromiumLink = dialog.querySelector("[data-extension-chromium-link]");
  let activeTrigger = null;

  const closeDialog = () => {
    if (dialog.open) dialog.close();
  };

  openButtons.forEach((button) => {
    button.addEventListener("click", () => {
      if (dialog.open) return;
      activeTrigger = button;
      dialog.showModal();
    });
  });

  closeButton?.addEventListener("click", closeDialog);
  chromiumLink?.addEventListener("click", closeDialog);

  dialog.addEventListener("click", (event) => {
    if (event.target === dialog) closeDialog();
  });

  dialog.addEventListener("close", () => {
    if (activeTrigger instanceof HTMLElement) activeTrigger.focus();
    activeTrigger = null;
  });
}
