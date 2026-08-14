#!/usr/bin/env node
// pnpm run build:firefox
//
// Replaces sign-firefox.bat + publish-firefox-xpi.bat + generate-firefox-updates.ps1.
// Builds the Firefox target with Vite, submits it to Mozilla AMO for UNLISTED
// signing (self-distribution), then publishes the signed XPI as
// BrowserExtension/PDownloader.xpi alongside a freshly generated updates.json.
//
// Required environment variables:
//   WEB_EXT_API_KEY     - AMO JWT issuer
//   WEB_EXT_API_SECRET  - AMO JWT secret
// (never commit these; export them in your shell before running this script)

import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, statSync, copyFileSync, writeFileSync, readFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';
import webExt from 'web-ext';
import AdmZip from 'adm-zip';

const extensionRoot = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const firefoxDir = path.join(extensionRoot, 'dist', 'firefox');
const artifactsDir = path.join(extensionRoot, 'web-ext-artifacts');
const publishedXpi = path.join(extensionRoot, 'PDownloader.xpi');
const firefoxConfigPath = path.join(extensionRoot, 'manifests', 'firefox.json');
const updatesJsonPath = path.join(extensionRoot, 'updates.json');

function fail(message) {
  console.error(`[ERROR] ${message}`);
  process.exit(1);
}

const apiKey = process.env.WEB_EXT_API_KEY;
const apiSecret = process.env.WEB_EXT_API_SECRET;

if (!apiKey) {
  fail(
    'WEB_EXT_API_KEY is not set.\n' +
      '  Open AMO Developer Hub, create API credentials, then:\n' +
      '    export WEB_EXT_API_KEY=your-jwt-issuer\n' +
      '    export WEB_EXT_API_SECRET=your-jwt-secret\n' +
      '    pnpm run build:firefox\n' +
      '  Do not commit or share the API secret.'
  );
}
if (!apiSecret) {
  fail('WEB_EXT_API_SECRET is not set. Do not put the secret in source code or commit it to Git.');
}

console.log('[PDownloader] Building Firefox extension...');
await build({ root: extensionRoot, mode: 'firefox', configFile: path.join(extensionRoot, 'vite.config.mjs') });

if (!existsSync(path.join(firefoxDir, 'manifest.json'))) {
  fail(`Firefox build output was not found: ${firefoxDir}`);
}

mkdirSync(artifactsDir, { recursive: true });

console.log('\nSubmitting extension to Mozilla for UNLISTED signing...');
console.log(`Extension source: ${firefoxDir}`);
console.log(`Signed artifacts: ${artifactsDir}\n`);

let signResult;
try {
  signResult = await webExt.cmd.sign(
    {
      apiKey,
      apiSecret,
      channel: 'unlisted',
      sourceDir: firefoxDir,
      artifactsDir
    },
    { shouldExitProgram: false }
  );
} catch (error) {
  fail(`Mozilla signing failed: ${error?.message ?? error}`);
}

if (!signResult?.success) {
  fail('Mozilla signing did not succeed. Review the web-ext output above. AMO may require manual review.');
}

console.log('\nPublishing signed XPI to BrowserExtension/PDownloader.xpi...');

const signedXpi = readdirSync(artifactsDir)
  .filter((name) => name.endsWith('.xpi'))
  .map((name) => path.join(artifactsDir, name))
  .sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];

if (!signedXpi) {
  fail(`No signed XPI was found in: ${artifactsDir}`);
}

copyFileSync(signedXpi, publishedXpi);

console.log('Generating Firefox update manifest from the signed XPI...');
generateUpdatesJson(publishedXpi, firefoxConfigPath, updatesJsonPath);

console.log('\n[OK] Firefox XPI and update manifest published locally.');
console.log(`Source: ${signedXpi}`);
console.log(`Target XPI: ${publishedXpi}`);
console.log(`Update manifest: ${updatesJsonPath}`);
console.log('\nCommit and push BOTH files together:');
console.log('  BrowserExtension/PDownloader.xpi');
console.log('  BrowserExtension/updates.json');

function assertHttpsUrl(value, name) {
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${name} must be an absolute HTTPS URL: ${value}`);
  }
  if (url.protocol !== 'https:') {
    throw new Error(`${name} must be an absolute HTTPS URL: ${value}`);
  }
}

function generateUpdatesJson(xpiPath, configPath, outputPath) {
  const config = JSON.parse(readFileSyncUtf8(configPath));
  const extensionId = config.extension_id;
  const updateLink = config.update_link;
  const strictMinVersion = config.strict_min_version;

  if (!extensionId) {
    throw new Error('Firefox extension_id is missing from the Firefox build configuration.');
  }
  assertHttpsUrl(updateLink, 'Firefox update_link');

  const zip = new AdmZip(xpiPath);
  const entries = new Set(zip.getEntries().map((entry) => entry.entryName));

  const manifestEntry = zip.getEntry('manifest.json');
  if (!manifestEntry) {
    throw new Error('Invalid XPI: manifest.json was not found at the archive root.');
  }

  const hasMozillaSignature = entries.has('META-INF/mozilla.rsa') || entries.has('META-INF/cose.sig');
  if (!hasMozillaSignature) {
    throw new Error('Refusing to publish updates.json because the XPI does not contain a Mozilla signature.');
  }

  const manifest = JSON.parse(zip.readAsText(manifestEntry));
  const version = manifest.version;
  if (!version) {
    throw new Error('Invalid XPI: manifest.json does not contain a version.');
  }

  const actualExtensionId = manifest.browser_specific_settings?.gecko?.id;
  if (actualExtensionId && actualExtensionId !== extensionId) {
    throw new Error(`XPI extension ID '${actualExtensionId}' does not match configured ID '${extensionId}'.`);
  }

  const sha256 = createHash('sha256').update(readFileSync(xpiPath)).digest('hex');

  const update = { version, update_link: updateLink, update_hash: `sha256:${sha256}` };
  if (strictMinVersion) {
    update.applications = { gecko: { strict_min_version: strictMinVersion } };
  }

  const updateManifest = { addons: { [extensionId]: { updates: [update] } } };
  writeFileSync(outputPath, `${JSON.stringify(updateManifest, null, 2)}\r\n`, 'utf8');
}

function readFileSyncUtf8(filePath) {
  return readFileSync(filePath, 'utf8');
}
