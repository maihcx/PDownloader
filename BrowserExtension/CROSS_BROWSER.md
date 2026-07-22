# PDownloader BrowserExtension - Chromium + Firefox

BrowserExtension now uses one shared source tree and builds platform-specific manifests.

## Build both browsers

```bat
build-extension.bat
```

or:

```bash
node build-extension.mjs all
```

Outputs:

- `dist/chromium/` - Chrome, Edge, and other Chromium browsers.
- `dist/firefox/` - Firefox Manifest V3 build.

## Test Chromium

Open `chrome://extensions` or `edge://extensions`, enable Developer mode, choose **Load unpacked**, and select `dist/chromium`.

## Test Firefox

Open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on**, and select `dist/firefox/manifest.json`.

The generated Firefox package uses `background.scripts` instead of Chromium's service worker and removes Chromium's manifest `key`. It keeps the same media capture, cookie jar, HLS/DASH, active-audio tracking, popup, and native HTTP bridge behavior.

For permanent Firefox installation, the XPI must be signed by Mozilla Add-ons (AMO) or deployed through an enterprise policy that permits your distribution method.

## Package browser-specific artifacts

From the repository root:

```bat
pack-crx.bat
```

builds `dist/chromium` first, then signs the Chromium CRX with the existing `signing-key.pem`.

```bat
pack-firefox.bat
```

builds `dist/firefox` and creates `BrowserExtension/PDownloader-Firefox-unsigned.xpi` for temporary testing. Mozilla signing is still required for normal permanent Firefox installation.

### Firefox XPI packaging note

`pack-firefox.bat` uses `pack-firefox.ps1` to create every archive entry with `/` separators and validates the exact entries `manifest.json`, `_locales/en/messages.json`, and `_locales/vi/messages.json`. This avoids Gecko failing to resolve localized resources from an XPI created with Windows-style archive paths.

For development, loading `dist/firefox/manifest.json` directly from `about:debugging` is the fastest path because it bypasses XPI packaging entirely.
