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

  // Keep existing user preferences during extension updates.
  // Only keys that have never been stored are initialized with defaults.
  async function seedDefaultsOnInstall() {
    const defaults = {
      ...DEFAULT_SETTINGS,
      extensions: DEFAULT_EXTENSIONS
    };

    const existing = await chrome.storage.local.get(Object.keys(defaults));
    const missing = {};

    for (const [key, value] of Object.entries(defaults)) {
      if (typeof existing[key] === 'undefined') {
        missing[key] = value;
      }
    }

    if (Object.keys(missing).length > 0) {
      await chrome.storage.local.set(missing);
    }
  }

  PD.Storage = {
    getSettings, saveSettings, getBlacklist, addBlacklist, removeBlacklist,
    seedDefaultsOnInstall
  };
})(self);
