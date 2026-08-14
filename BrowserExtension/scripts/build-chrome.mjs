#!/usr/bin/env node
// pnpm run build:chrome
//
// Replaces the old pack-crx.bat. The self-hosted CRX (chrome --pack-extension
// + signing-key.pem) has been removed: the extension now ships through the
// Chrome Web Store (see PDownloader.Installer/Services/BrowserExtensionInstallerService.cs),
// so all this needs to produce is the "chromium" dev build and the
// no-"key" "store" build zipped up for the Developer Dashboard.

import { existsSync, rmSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';
import AdmZip from 'adm-zip';

const extensionRoot = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const storeDir = path.join(extensionRoot, 'dist', 'store');
const storeZip = path.join(extensionRoot, 'PDownloader-store.zip');

async function buildTarget(target) {
  await build({ root: extensionRoot, mode: target, configFile: path.join(extensionRoot, 'vite.config.mjs') });
}

console.log('[PDownloader] Building Chromium (dev/unpacked) extension...');
await buildTarget('chromium');

console.log('[PDownloader] Building Chrome Web Store extension (no "key")...');
await buildTarget('store');

if (!existsSync(storeDir)) {
  console.error(`[ERROR] Missing build output: ${storeDir}`);
  process.exit(1);
}

console.log('[PDownloader] Packing Chrome Web Store zip...');
if (existsSync(storeZip)) rmSync(storeZip);

const zip = new AdmZip();
zip.addLocalFolder(storeDir);
zip.writeZip(storeZip);

console.log(`\nDone: ${storeZip}`);
console.log('  -> Upload this zip to the Chrome Web Store Developer Dashboard.');
console.log('  -> On first upload, the Store issues its own Extension ID (different from');
console.log('     any old self-hosted CRX id, since this zip has no "key" field). Update');
console.log('     BrowserExtensionInstallerService.ExtensionId and');
console.log('     AllowedChromiumExtensionOrigins in PDownloader.Core/Service/HttpBridgeService.cs,');
console.log('     then rebuild the app.');
console.log('\nReminder: bump the version in manifest.json before packing a release.');
