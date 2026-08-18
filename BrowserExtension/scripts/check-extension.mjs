#!/usr/bin/env node

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { BACKGROUND_SCRIPTS } from './manifest.mjs';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const failures = [];

function fail(message) {
  failures.push(message);
}

function relative(filePath) {
  return path.relative(root, filePath).replaceAll(path.sep, '/');
}

function walk(directory, predicate) {
  const files = [];
  for (const entry of readdirSync(directory)) {
    if (['dist', 'node_modules', 'web-ext-artifacts'].includes(entry)) continue;
    const fullPath = path.join(directory, entry);
    const stat = statSync(fullPath);
    if (stat.isDirectory()) files.push(...walk(fullPath, predicate));
    else if (predicate(fullPath)) files.push(fullPath);
  }
  return files;
}

function read(filePath) {
  return readFileSync(filePath, 'utf8');
}

function assertFile(runtimePath, source = 'manifest') {
  if (!runtimePath || /^(?:https?:|__MSG_)/i.test(runtimePath)) return;
  if (!existsSync(path.join(root, runtimePath))) {
    fail(`${source} references missing file: ${runtimePath}`);
  }
}

for (const file of walk(root, file => /\.(?:js|mjs)$/i.test(file))) {
  const result = spawnSync(process.execPath, ['--check', file], { encoding: 'utf8' });
  if (result.status !== 0) {
    fail(`${relative(file)} has invalid JavaScript syntax:\n${result.stderr.trim()}`);
  }
}

for (const file of walk(root, file => /\.json$/i.test(file))) {
  try {
    JSON.parse(read(file));
  } catch (error) {
    fail(`${relative(file)} has invalid JSON: ${error.message}`);
  }
}

const manifest = JSON.parse(read(path.join(root, 'manifest.json')));
assertFile(manifest.action?.default_popup, 'manifest.action');
for (const icon of Object.values(manifest.icons || {})) assertFile(icon, 'manifest.icons');
for (const icon of Object.values(manifest.action?.default_icon || {})) assertFile(icon, 'manifest.action.default_icon');
for (const script of manifest.content_scripts || []) {
  for (const file of script.js || []) assertFile(file, 'manifest.content_scripts');
  for (const file of script.css || []) assertFile(file, 'manifest.content_scripts');
}
for (const resourceGroup of manifest.web_accessible_resources || []) {
  for (const file of resourceGroup.resources || []) assertFile(file, 'manifest.web_accessible_resources');
}

for (const file of BACKGROUND_SCRIPTS) assertFile(file, 'Firefox background scripts');

const backgroundImports = [...read(path.join(root, 'background.js')).matchAll(/['"]([^'"]+\.js)['"]/g)]
  .map(match => match[1]);
if (JSON.stringify(backgroundImports) !== JSON.stringify(BACKGROUND_SCRIPTS)) {
  fail('background.js imports and Firefox BACKGROUND_SCRIPTS are not identical or ordered the same.');
}

const localeDirectories = readdirSync(path.join(root, '_locales'));
const localeMessages = new Map(localeDirectories.map(locale => [
  locale,
  JSON.parse(read(path.join(root, '_locales', locale, 'messages.json')))
]));
const referenceKeys = new Set(Object.keys(localeMessages.get(manifest.default_locale) || {}));
for (const [locale, messages] of localeMessages) {
  const keys = new Set(Object.keys(messages));
  for (const key of referenceKeys) {
    if (!keys.has(key)) fail(`Locale ${locale} is missing message key: ${key}`);
  }
  for (const key of keys) {
    if (!referenceKeys.has(key)) fail(`Locale ${locale} has unknown message key: ${key}`);
  }
}

const sourceText = walk(root, file => /\.(?:js|mjs|html)$/i.test(file))
  .map(read)
  .join('\n');
const usedMessageKeys = new Set([
  ...[...sourceText.matchAll(/PD\.I18n\.t\(\s*['"]([^'"]+)['"]/g)].map(match => match[1]),
  ...[...sourceText.matchAll(/__MSG_([A-Za-z0-9_]+)__/g)].map(match => match[1])
]);
for (const key of usedMessageKeys) {
  if (!referenceKeys.has(key)) fail(`Source references missing locale key: ${key}`);
}

if (!read(path.join(root, 'common', 'theme.css')).includes('--pd-warning:')) {
  fail('common/theme.css must define --pd-warning.');
}

if (failures.length) {
  console.error(`[PDownloader] Extension check failed (${failures.length}):`);
  for (const message of failures) console.error(`- ${message}`);
  process.exit(1);
}

console.log('[PDownloader] Extension source checks passed.');
