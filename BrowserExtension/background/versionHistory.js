(function (root) {
  const PD = root.PD || (root.PD = {});

  const STORAGE_KEYS = {
    history: 'versionHistory',
    lastKnownVersion: 'lastKnownExtensionVersion',
    lastNotifiedVersion: 'lastUpdateNotificationVersion'
  };

  const MAX_HISTORY_ITEMS = 50;
  let operationQueue = Promise.resolve();

  function currentVersion() {
    return chrome.runtime.getManifest().version;
  }

  function nowIso() {
    return new Date().toISOString();
  }

  function normalizeHistory(value) {
    if (!Array.isArray(value)) return [];

    return value
      .filter(item => item && typeof item.version === 'string' && item.version.trim())
      .map(item => ({
        version: item.version.trim(),
        detectedAt: typeof item.detectedAt === 'string' ? item.detectedAt : nowIso(),
        source: typeof item.source === 'string' ? item.source : 'unknown',
        previousVersion:
          typeof item.previousVersion === 'string' && item.previousVersion.trim()
            ? item.previousVersion.trim()
            : null
      }))
      .slice(-MAX_HISTORY_ITEMS);
  }

  function findVersionIndex(history, version) {
    return history.findIndex(item => item.version === version);
  }

  function addOrUpdateVersion(history, version, source, previousVersion = null) {
    if (!version) return history;

    const index = findVersionIndex(history, version);
    if (index >= 0) {
      const existing = history[index];
      history[index] = {
        ...existing,
        source: existing.source === 'unknown' ? source : existing.source,
        previousVersion: existing.previousVersion || previousVersion || null
      };
      return history;
    }

    history.push({
      version,
      detectedAt: nowIso(),
      source,
      previousVersion: previousVersion || null
    });

    if (history.length > MAX_HISTORY_ITEMS) {
      history.splice(0, history.length - MAX_HISTORY_ITEMS);
    }

    return history;
  }

  function addPreviousVersionBeforeCurrent(history, previousVersion, current) {
    if (!previousVersion || previousVersion === current || findVersionIndex(history, previousVersion) >= 0) {
      return history;
    }

    const currentIndex = findVersionIndex(history, current);
    const entry = {
      version: previousVersion,
      detectedAt: nowIso(),
      source: 'update-previous-version',
      previousVersion: null
    };

    if (currentIndex >= 0) {
      history.splice(currentIndex, 0, entry);
    } else {
      history.push(entry);
    }

    if (history.length > MAX_HISTORY_ITEMS) {
      history.splice(0, history.length - MAX_HISTORY_ITEMS);
    }

    return history;
  }

  function getLastHistoryVersion(history) {
    return history.length > 0 ? history[history.length - 1].version : null;
  }

  async function loadState() {
    const result = await chrome.storage.local.get(Object.values(STORAGE_KEYS));
    return {
      history: normalizeHistory(result[STORAGE_KEYS.history]),
      lastKnownVersion:
        typeof result[STORAGE_KEYS.lastKnownVersion] === 'string'
          ? result[STORAGE_KEYS.lastKnownVersion]
          : null,
      lastNotifiedVersion:
        typeof result[STORAGE_KEYS.lastNotifiedVersion] === 'string'
          ? result[STORAGE_KEYS.lastNotifiedVersion]
          : null
    };
  }

  async function saveState(state) {
    await chrome.storage.local.set({
      [STORAGE_KEYS.history]: state.history,
      [STORAGE_KEYS.lastKnownVersion]: state.lastKnownVersion,
      [STORAGE_KEYS.lastNotifiedVersion]: state.lastNotifiedVersion
    });
  }

  async function notifyUpdateOnce(state, previousVersion, version) {
    if (!previousVersion || previousVersion === version) return;
    if (state.lastNotifiedVersion === version) return;

    await PD.Notify.showExtensionUpdated();
    state.lastNotifiedVersion = version;
  }

  function enqueue(operation) {
    const next = operationQueue.then(operation, operation);
    operationQueue = next.catch(() => {});
    return next;
  }

  function checkCurrentVersion(source = 'service-worker-start') {
    return enqueue(async () => {
      const version = currentVersion();
      const state = await loadState();
      const previousVersion = state.lastKnownVersion || getLastHistoryVersion(state.history);

      addOrUpdateVersion(state.history, version, source, previousVersion);

      if (previousVersion && previousVersion !== version) {
        await notifyUpdateOnce(state, previousVersion, version);
      }

      state.lastKnownVersion = version;
      await saveState(state);

      return {
        updated: Boolean(previousVersion && previousVersion !== version),
        previousVersion,
        currentVersion: version
      };
    });
  }

  function handleInstalled(details) {
    return enqueue(async () => {
      const version = currentVersion();
      const state = await loadState();
      const isUpdate = details?.reason === 'update';
      const previousVersion = isUpdate
        ? details.previousVersion || state.lastKnownVersion || getLastHistoryVersion(state.history)
        : null;

      if (isUpdate && previousVersion) {
        addPreviousVersionBeforeCurrent(state.history, previousVersion, version);
      }

      addOrUpdateVersion(
        state.history,
        version,
        details?.reason || 'installed',
        previousVersion
      );

      if (isUpdate) {
        await notifyUpdateOnce(state, previousVersion, version);
      }

      state.lastKnownVersion = version;
      await saveState(state);

      return {
        updated: isUpdate,
        previousVersion,
        currentVersion: version
      };
    });
  }

  function getHistory() {
    return chrome.storage.local.get([STORAGE_KEYS.history]).then(result =>
      normalizeHistory(result[STORAGE_KEYS.history])
    );
  }

  PD.VersionHistory = {
    checkCurrentVersion,
    handleInstalled,
    getHistory
  };
})(self);
