#!/usr/bin/env node
// pnpm run build:firefox-nosign
//
// Local/dev-only build: runs the same Vite "firefox" target as build:firefox
// but skips the Mozilla AMO signing submission entirely. Produces an
// unsigned dist/firefox/ folder plus a zip for convenience. Use this for
// day-to-day testing via about:debugging -> "Load Temporary Add-on..."
// (either the unpacked dist/firefox/manifest.json, or the zip below).
//
// This does NOT touch PDownloader.xpi or updates.json - those are only
// ever produced by the signed `pnpm run build:firefox` flow, since Firefox
// will refuse to permanently install an unsigned XPI.

import { existsSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';
import AdmZip from 'adm-zip';

const extensionRoot = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const firefoxDir = path.join(extensionRoot, 'dist', 'firefox');
const unsignedZip = path.join(extensionRoot, 'PDownloader-firefox-unsigned.zip');

function fail(message) {
  console.error(`[ERROR] ${message}`);
  process.exit(1);
}

console.log('[PDownloader] Building Firefox extension (unsigned, dev only)...');
await build({ root: extensionRoot, mode: 'firefox', configFile: path.join(extensionRoot, 'vite.config.mjs') });

if (!existsSync(path.join(firefoxDir, 'manifest.json'))) {
  fail(`Firefox build output was not found: ${firefoxDir}`);
}

if (existsSync(unsignedZip)) rmSync(unsignedZip);

const zip = new AdmZip();
zip.addLocalFolder(firefoxDir);
zip.writeZip(unsignedZip);

console.log(`\n[OK] Unsigned build ready.`);
console.log(`Unpacked: ${firefoxDir}`);
console.log(`Zip:      ${unsignedZip}`);
console.log('\nLoad it in Firefox for testing:');
console.log('  about:debugging#/runtime/this-firefox -> Load Temporary Add-on...');
console.log(`  -> select ${path.join(firefoxDir, 'manifest.json')} (or the zip above)`);
console.log('\nThis is NOT signed and will be removed on browser restart.');
console.log('For a permanent/self-distributed install, run: pnpm run build:firefox');
