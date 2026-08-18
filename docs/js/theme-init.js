(() => {
  const storageKey = "pdownloader-theme";
  let savedTheme = null;
  try {
    savedTheme = localStorage.getItem(storageKey);
  } catch {
    // Storage can be unavailable in strict privacy modes.
  }
  const preferredTheme = window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  const theme = savedTheme === "dark" || savedTheme === "light" ? savedTheme : preferredTheme;

  document.documentElement.dataset.theme = theme;
  document.documentElement.dataset.themeSource = savedTheme ? "saved" : "system";
})();
