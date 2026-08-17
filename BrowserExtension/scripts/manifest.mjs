// Per-target manifest.json generation, shared by vite.config.mjs.
// Ported from the old build-extension.mjs (plain Node copy script).

export const BACKGROUND_SCRIPTS = [
  'common/browserApi.js',
  'common/i18n.js',
  'background/constants.js',
  'background/state.js',
  'background/utils.js',
  'background/storage.js',
  'background/badge.js',
  'background/notifications.js',
  'background/versionHistory.js',
  'background/api.js',
  'background/contextMenu.js',
  'background/mediaCandidateRegistry.js',
  'background/mediaCapture.js',
  'background/hlsCapture.js',
  'background/contentDisposition.js',
  'background/downloadIntercept.js',
  'background/messageRouter.js',
  'background/bootstrap.js'
];

// Everything the runtime needs at extension load time, besides the
// generated manifest.json/background.js which each target writes itself.
export const RUNTIME_ENTRIES = ['_locales', 'background', 'common', 'content', 'icons', 'popup'];

export const TARGETS = ['chromium', 'store', 'firefox'];

function requireHttpsUrl(value, fieldName) {
  if (typeof value !== 'string' || !value.startsWith('https://')) {
    throw new Error(`[PDownloader] ${fieldName} must be an HTTPS URL.`);
  }
  return value;
}

/**
 * @param {'chromium'|'store'|'firefox'} target
 * @param {object} baseManifest parsed manifest.json
 * @param {object} firefoxConfig parsed manifests/firefox.json
 * @param {{ listed?: boolean }} [options] listed=true khi submit cho AMO
 */
export function buildManifestForTarget(target, baseManifest, firefoxConfig, options = {}) {
  const { listed = false } = options;
  const manifest = structuredClone(baseManifest);

  if (target === 'chromium') {
    manifest.background = { service_worker: 'background.js' };
    delete manifest.browser_specific_settings;
    return manifest;
  }

  if (target === 'store') {
    manifest.background = { service_worker: 'background.js' };
    delete manifest.browser_specific_settings;
    delete manifest.key;
    return manifest;
  }

  if (target === 'firefox') {
    delete manifest.key;
    manifest.background = { scripts: BACKGROUND_SCRIPTS };
    manifest.browser_specific_settings = {
      gecko: {
        id: firefoxConfig.extension_id,
        strict_min_version: firefoxConfig.strict_min_version,
        ...(listed ? {} : { update_url: requireHttpsUrl(firefoxConfig.update_url, 'Firefox update_url') }),
        data_collection_permissions: firefoxConfig.data_collection_permissions
      },
      gecko_android: {
        strict_min_version: firefoxConfig.strict_min_version_android
      }
    };
    return manifest;
  }

  throw new Error(`[PDownloader] Unknown build target: ${target}`);
}
