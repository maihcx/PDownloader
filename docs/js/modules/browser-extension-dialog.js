export function initBrowserExtensionDialog() {
  const dialog = document.querySelector("#browser-extension-dialog");
  if (!(dialog instanceof HTMLDialogElement)) return;

  const openButtons = document.querySelectorAll("[data-extension-dialog-open]");
  const closeButton = dialog.querySelector("[data-extension-dialog-close]");
  const chromiumLink = dialog.querySelector("[data-extension-chromium-link]");

  const closeDialog = () => {
    if (dialog.open) dialog.close();
  };

  openButtons.forEach((button) => {
    button.addEventListener("click", () => {
      if (!dialog.open) dialog.showModal();
    });
  });

  closeButton?.addEventListener("click", closeDialog);
  chromiumLink?.addEventListener("click", closeDialog);

  dialog.addEventListener("click", (event) => {
    const bounds = dialog.getBoundingClientRect();
    const isInside =
      event.clientX >= bounds.left &&
      event.clientX <= bounds.right &&
      event.clientY >= bounds.top &&
      event.clientY <= bounds.bottom;

    if (!isInside) closeDialog();
  });
}
