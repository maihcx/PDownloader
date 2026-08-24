# PDownloader

A multi-threaded download manager for Windows, built with .NET 10, WPF, and MVVM. PDownloader pairs a native desktop application with a browser extension (Chrome, Edge, Brave, Cốc Cốc) to capture, queue, and accelerate downloads — including YouTube video/audio via `yt-dlp` and HLS/DASH streaming media.

---

## Source Code

PDownloader is developed across the following repositories:

| Component | Repository | Purpose |
| --- | --- | --- |
| **Main App** | **[maihcx/PDownloader](https://github.com/maihcx/PDownloader) ← You are here** | Windows application, download engine, and releases. |
| Browser Extension | [maihcx/PDownloader-browser-ext](https://github.com/maihcx/PDownloader-browser-ext) | Browser integration, download interception, and media capture. |
| Website | [maihcx/PDownloader-site](https://github.com/maihcx/PDownloader-site) | Website interface, translations, documentation reader, and Markdown articles. |

---

## Highlights

- **Multi-segment downloading** — splits a file into parallel byte-range requests (default 8 threads) to accelerate transfers, with automatic fallback to single-stream when the server doesn't support ranges.
- **Resume & retry** — persists segment state to disk so interrupted downloads resume where they left off, with exponential back-off retries per segment.
- **HLS/DASH streaming support** — detects `.m3u8` playlists and downloads fragments concurrently (via `SemaphoreSlim`-bounded parallelism), then merges them into a single output file.
- **YouTube & site downloads via yt-dlp** — resolves formats and stream URLs through `yt-dlp` (including cookie-based authentication to bypass bot checks), while the actual transfer is handled by PDownloader's own download engine.
- **Browser extension (MV3)** — context-menu capture, a popup for manual URL entry, and automatic detection of downloadable links, all communicating with the desktop app over a local HTTP bridge.
- **System tray & background service** — a lightweight background service coordinates the main UI, the download engine window, and the tray icon over local IPC.

---

## Architecture

PDownloader is split into several cooperating processes that communicate over a custom local IPC layer (**CFS**, `PDownloader.CFS`) and a local HTTP bridge used by the browser extension.

```
Browser (Chrome / Edge / Brave / Cốc Cốc)
  Extension: context menu / popup / auto-capture
        │  HTTP POST http://localhost:6287
        ▼
PDownloader.Core  (background service)
  • HTTP bridge on :6287 (/ping, /download, /youtube/analyze, /youtube/download)
  • CFS coordinator — routes commands between processes
        │
        ├── CFS ──▶ PDownloader        (main WPF UI: settings, app entry point)
        ├── CFS ──▶ PDownloader.Tray   (system tray icon, navigation events)
        └── CFS ──▶ PDownloader.Runner (download engine window)
                       • DownloadEngine: multi-segment / HLS fragment downloads
                       • Resume, retry, and segment merging
```

### Projects

| Project | Role |
|---|---|
| `PDownloader` | Main WPF application: startup, app configuration, settings UI. Sends `download` commands to Core over CFS. |
| `PDownloader.Core` | Background service that coordinates everything. Hosts the HTTP bridge on port `6287` and relays commands between the Main UI, Tray, Runner, and the browser extension. |
| `PDownloader.Runner` | WPF download manager window. Receives `download` commands from Core and runs the `DownloadEngine`. |
| `PDownloader.Tray` | System tray icon that forwards navigation events to Core. |
| `PDownloader.CFS` | Shared library implementing the inter-process communication layer used by all components. |
| `PDownloader.Installer` | Windows installer/setup application. |
| `PDownloader.BugTracker` | Companion crash-reporting window launched on unhandled exceptions. |
| `BrowserExtension` | Cross-browser Manifest V3 extension (Chrome, Edge, Brave, Cốc Cốc, Firefox, Zen Browser) that captures links and posts them to Core's HTTP bridge. |
| `WPF-UI.LIB` | Forked/customized WPF-UI controls used across the desktop apps. |

---

## Download Engine

Located in `PDownloader.Core/Download/DownloadEngine.cs`, the engine works roughly like this:

1. **Probe** the target URL (`HEAD`, falling back to a ranged `GET`) to determine total size and whether the server supports `Accept-Ranges: bytes`.
2. **Split into segments** — when range requests are supported, the file is divided across multiple parallel byte-range downloads.
3. **Write to temp files** — each segment is written to its own `.part` file under a per-download temp folder.
4. **Persist state** — segment progress is saved to disk so a download can resume after the app is closed or crashes.
5. **Retry with back-off** — failed segments are retried with exponential delay; a server that rejects `Range` requests (HTTP 403) triggers an automatic fallback to a non-range retry for that segment.
6. **HLS playlists** — `.m3u8` playlists are detected and their fragments downloaded concurrently under a bounded semaphore, then merged.
7. **Merge & cleanup** — completed segments/fragments are concatenated into the final file and the temp folder is removed.

If a server doesn't support ranged requests at all, the engine transparently falls back to a single-stream download.

---

## Browser Extension

The extension lives in `BrowserExtension/` and is built as a Manifest V3 extension. It is officially supported on **Google Chrome**, **Microsoft Edge**, **Brave**, **Cốc Cốc**, **Mozilla Firefox**, and **Zen Browser**.

**Installation**
When PDownloader is installed correctly, the extension is installed and enabled automatically for all supported browsers (via Windows registry policy) — no manual setup is required.

**Manual/development install** (for contributors or debugging)
For Chromium browsers:
1. Open `chrome://extensions` (`edge://extensions`, `brave://extensions`, or the Cốc Cốc equivalent).
2. Enable Developer Mode.
3. Click **Load unpacked** and select `BrowserExtension/dist/chromium/`.

For Firefox/Zen Browser:
1. Open `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on**.
3. Select `BrowserExtension/dist/firefox/manifest.json`.

**How a link reaches the download engine**
```
User clicks "Download with PDownloader" (context menu or popup)
  → background.js: POST http://localhost:6287/download { url, saveTo, fileName }
  → PDownloader.Core (HttpBridgeService) parses the request
  → forwarded to PDownloader.Runner over CFS
  → RunnerWindow enqueues the download → DownloadEngine starts
```

**Features**
- Context menu entries on links, videos, and pages.
- Popup for manually entering a URL, choosing a save folder, and listing downloadable links found on the current page.
- Automatic capture of clicks on common downloadable file types.
- Desktop notifications for successful queuing or connection errors.
- Localized UI (English/Vietnamese via `_locales`).

---

## Local HTTP Bridge (`PDownloader.Core`)

`PDownloader.Core` exposes a minimal local HTTP API on `http://localhost:6287/`, used by the browser extension:

| Endpoint | Method | Description |
|---|---|---|
| `/ping` | GET | Health check; returns app name and version. |
| `/download` | POST | Queues a regular file download (`{ url, saveTo, fileName }`). |
| `/youtube/analyze` | POST | Resolves available formats for a YouTube (or supported site) URL via `yt-dlp`. |
| `/youtube/download` | POST | Starts a YouTube/site download using a resolved format. |

---

## Building & Running

**Requirements**
- .NET 10 SDK
- Windows 10 or later, x64
- `yt-dlp` available for YouTube/site resolution (bundled or configured via settings)

```bash
# Restore & build the whole solution
dotnet build PDownloader.sln -c Release

# Run the main UI in development (starts the Core background service automatically)
dotnet run --project PDownloader
```

For the browser extension, see the "Manual/development install" steps above. `build.bat` is provided at the repository root for producing the installer release artifact. The extension itself is built with Vite from `BrowserExtension/` (Node.js required):

```bash
cd BrowserExtension
pnpm install

pnpm run build:chrome    # dist/chromium, dist/store, PDownloader-store.zip
pnpm run build:firefox   # dist/firefox, signs with AMO, publishes PDownloader.xpi + updates.json
```

`build:firefox` submits to Mozilla AMO for unlisted signing and requires `WEB_EXT_API_KEY`/`WEB_EXT_API_SECRET` environment variables (never commit these).

### Silent installer

Use `--silent` (or `--quiet`, `-s`, `/S`) to install without displaying the
installer window or asking for input:

```powershell
PDownloader.Installer.exe --silent
```

The silent mode supports these optional parameters:

| Parameter | Effect |
| --- | --- |
| `--install-dir "C:\Apps\PDownloader"` | Sets a custom installation directory. `/DIR=...` is also accepted. |
| `--no-desktop-shortcut` | Does not create the desktop shortcut. |
| `--no-start-menu-shortcut` | Does not create the Start menu shortcut. |
| `--no-browser-extension` | Does not install the PDownloader extension for supported browsers. |
| `--run-at-startup` / `--no-run-at-startup` | Enables or disables starting PDownloader with Windows. The existing setting is preserved when neither is supplied. |
| `--launch-after-install` | Launches PDownloader after installation. Silent mode does not launch it by default. |
| `--uninstall --silent` | Uninstalls PDownloader without displaying the installer window. |

The process exits with code `0` on success and `1` on failure. Windows may
still display a UAC elevation prompt because installation requires administrator
privileges.

---

## License

PDownloader is licensed under the **GNU General Public License v3.0**. See [`LICENSE`](./LICENSE) (and [`LICENSE.vi`](./LICENSE.vi) for a Vietnamese translation) for the full text.
