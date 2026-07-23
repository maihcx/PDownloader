#!/usr/bin/env node

import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const extensionRoot = path.dirname(fileURLToPath(import.meta.url));
const distRoot = path.join(extensionRoot, 'dist');
const targetArg = (process.argv[2] || 'all').toLowerCase();
const validTargets = new Set(['all', 'chromium', 'firefox']);

if (!validTargets.has(targetArg)) {
  console.error('Usage: node build-extension.mjs [all|chromium|firefox]');
  process.exit(1);
}

const backgroundScripts = [
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

const runtimeEntries = [
  '_locales',
  'background',
  'common',
  'content',
  'icons',
  'popup'
];

async function readJson(file) {
  return JSON.parse(await readFile(file, 'utf8'));
}

async function copyRuntimeFiles(outputDir) {
  await rm(outputDir, { recursive: true, force: true });
  await mkdir(outputDir, { recursive: true });

  for (const entry of runtimeEntries) {
    await cp(path.join(extensionRoot, entry), path.join(outputDir, entry), {
      recursive: true
    });
  }
}

async function writeManifest(outputDir, manifest) {
  await writeFile(
    path.join(outputDir, 'manifest.json'),
    `${JSON.stringify(manifest, null, 2)}\r\n`,
    'utf8'
  );
}

function requireHttpsUrl(value, fieldName) {
  if (typeof value !== 'string' || !value.startsWith('https://')) {
    throw new Error(`[PDownloader] ${fieldName} must be an HTTPS URL.`);
  }

  return value;
}

async function buildChromium(baseManifest) {
  const outputDir = path.join(distRoot, 'chromium');
  await copyRuntimeFiles(outputDir);

  const manifest = structuredClone(baseManifest);
  manifest.background = { service_worker: 'background.js' };
  delete manifest.browser_specific_settings;

  await cp(path.join(extensionRoot, 'background.js'), path.join(outputDir, 'background.js'));
  await writeManifest(outputDir, manifest);
  console.log(`[PDownloader] Chromium build: ${outputDir}`);
}

async function buildFirefox(baseManifest, firefoxConfig) {
  const outputDir = path.join(distRoot, 'firefox');
  await copyRuntimeFiles(outputDir);

  const manifest = structuredClone(baseManifest);
  delete manifest.key;

  manifest.background = {
    scripts: backgroundScripts
  };

  manifest.browser_specific_settings = {
    gecko: {
      id: firefoxConfig.extension_id,
      strict_min_version: firefoxConfig.strict_min_version,
      update_url: requireHttpsUrl(firefoxConfig.update_url, 'Firefox update_url'),
      data_collection_permissions: firefoxConfig.data_collection_permissions
    },
    gecko_android: {
      strict_min_version: firefoxConfig.strict_min_version_android
    }
  };

  await writeManifest(outputDir, manifest);
  console.log(`[PDownloader] Firefox build: ${outputDir}`);
}

await mkdir(distRoot, { recursive: true });
const baseManifest = await readJson(path.join(extensionRoot, 'manifest.json'));
const firefoxConfig = await readJson(path.join(extensionRoot, 'manifests', 'firefox.json'));

if (targetArg === 'all' || targetArg === 'chromium') {
  await buildChromium(baseManifest);
}

if (targetArg === 'all' || targetArg === 'firefox') {
  await buildFirefox(baseManifest, firefoxConfig);
}
