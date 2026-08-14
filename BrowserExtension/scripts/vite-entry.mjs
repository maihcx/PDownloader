// Intentionally empty. See the comment in vite.config.mjs: this extension's
// runtime scripts are plain classic scripts wired via manifest.json, not
// ES modules, so there is nothing for Rollup to bundle here. This file only
// exists to give Vite a valid build entry; the resulting empty chunk is
// deleted by the pdownloaderManifestPlugin closeBundle hook.
export {};
