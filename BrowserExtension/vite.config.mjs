import { readFileSync, writeFileSync, copyFileSync, rmSync, existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import { viteStaticCopy } from 'vite-plugin-static-copy';
import { RUNTIME_ENTRIES, TARGETS, buildManifestForTarget } from './scripts/manifest.mjs';

const extensionRoot = path.dirname(fileURLToPath(import.meta.url));

function readJson(filePath) {
  return JSON.parse(readFileSync(filePath, 'utf8'));
}

// The extension's background/content/popup scripts are plain classic
// (non-ESM) WebExtension scripts that rely on globalThis namespacing
// (see common/browserApi.js, background/bootstrap.js, etc.) and are wired
// together declaratively via manifest.json ("content_scripts", "scripts").
// Rewriting ~25 interdependent files into ES modules is a separate,
// higher-risk project on its own, so this config uses Vite as the build
// orchestrator (dev server, static-copy, manifest templating) without
// changing that runtime loading model. Only popup assets are eligible for
// real bundling; everything else is copied through as-is.
function pdownloaderManifestPlugin(target) {
  const outDirName = target === 'store' ? 'store' : target;
  const outDir = path.join(extensionRoot, 'dist', outDirName);

  return {
    name: 'pdownloader-manifest',
    apply: 'build',
    enforce: 'post',
    closeBundle() {
      const baseManifest = readJson(path.join(extensionRoot, 'manifest.json'));
      const firefoxConfig = readJson(path.join(extensionRoot, 'manifests', 'firefox.json'));
      const manifest = buildManifestForTarget(target, baseManifest, firefoxConfig, {
        listed: process.env.FIREFOX_CHANNEL === 'listed'
      });

      writeFileSync(path.join(outDir, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\r\n`, 'utf8');

      if (target === 'chromium' || target === 'store') {
        copyFileSync(path.join(extensionRoot, 'background.js'), path.join(outDir, 'background.js'));
      }

      // Drop the empty placeholder chunk Rollup emits for our dummy entry -
      // it isn't referenced by manifest.json and has no runtime purpose.
      const placeholderDir = path.join(outDir, 'assets');
      if (existsSync(placeholderDir)) rmSync(placeholderDir, { recursive: true, force: true });

      console.log(`[PDownloader] ${target} build -> ${outDir}`);
    }
  };
}

export default defineConfig(({ mode }) => {
  const target = mode;

  if (!TARGETS.includes(target)) {
    throw new Error(`[PDownloader] Unknown target "${mode}". Run vite build --mode ${TARGETS.join('|')}.`);
  }

  return {
    root: extensionRoot,
    publicDir: false,
    build: {
      outDir: `dist/${target === 'store' ? 'store' : target}`,
      emptyOutDir: true,
      // No real HTML/JS entry is bundled here (see comment above) - this
      // no-op entry just gives Rollup a valid build graph so plugins run.
      rollupOptions: {
        input: path.join(extensionRoot, 'scripts', 'vite-entry.mjs')
      }
    },
    plugins: [
      viteStaticCopy({
        // dest is '.': the plugin already preserves each match's path
        // relative to project root (e.g. "background/badge.js"), so passing
        // dest: entry here would double up the folder name.
        targets: RUNTIME_ENTRIES.map((entry) => ({
          src: `${entry}/**/*`,
          dest: '.'
        }))
      }),
      pdownloaderManifestPlugin(target)
    ]
  };
});
