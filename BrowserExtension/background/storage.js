(function (root) {
  const PD = root.PD || (root.PD = {});
  const { SETTINGS_KEYS, DEFAULT_SETTINGS, DEFAULT_EXTENSIONS } = PD.Constants;

  function getSettings(keys = SETTINGS_KEYS) {
    return chrome.storage.local.get(keys);
  }

  function saveSettings(partial) {
    return chrome.storage.local.set(partial);
  }

  async function getBlacklist() {
    const { blacklistedDomains } = await chrome.storage.local.get(['blacklistedDomains']);
    return blacklistedDomains || [];
  }

  async function addBlacklist(domain) {
    const list = await getBlacklist();
    if (!list.includes(domain)) list.push(domain);
    await saveSettings({ blacklistedDomains: list });
    return list;
  }

  async function removeBlacklist(domain) {
    const list = (await getBlacklist()).filter(x => x !== domain);
    await saveSettings({ blacklistedDomains: list });
    return list;
  }

  function seedDefaultsOnInstall() {
    return chrome.storage.local.set({
      ...DEFAULT_SETTINGS,
      extensions: DEFAULT_EXTENSIONS
    });
  }

  PD.Storage = {
    getSettings, saveSettings, getBlacklist, addBlacklist, removeBlacklist,
    seedDefaultsOnInstall
  };
})(self);
