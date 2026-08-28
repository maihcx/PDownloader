# PDownloader

A multi-threaded download manager for Windows, built with .NET 10, WPF, and MVVM. PDownloader can integrate with its separately maintained browser extension to capture, queue, and accelerate downloads — including YouTube video/audio via `yt-dlp` and HLS/DASH streaming media.

---

## Source Code

PDownloader is developed across the following repositories:

| Repository | Purpose |
| --- | --- |
| **[Main App](https://github.com/maihcx/PDownloader) ← You are here** | Windows application, download engine, installer, and releases. |
| [Browser Extension](https://github.com/maihcx/PDownloader-browser-ext) | Browser integration, download interception, and media capture. |
| [Website](https://github.com/maihcx/PDownloader-site) | Website, translations, documentation reader, and Markdown articles. |

---

## Highlights

- **Multi-segment downloading** — splits a file into parallel byte-range requests (default 8 threads) to accelerate transfers, with automatic fallback to single-stream when the server doesn't support ranges.
- **Resume & retry** — persists segment state to disk so interrupted downloads resume where they left off, with exponential back-off retries per segment.
- **HLS/DASH streaming support** — detects `.m3u8` playlists and downloads fragments concurrently (via `SemaphoreSlim`-bounded parallelism), then merges them into a single output file.
- **YouTube & site downloads via yt-dlp** — resolves formats and stream URLs through `yt-dlp` (including cookie-based authentication to bypass bot checks), while the actual transfer is handled by PDownloader's own download engine.
- **Browser integration** — works with the separately maintained [PDownloader browser extension](https://github.com/maihcx/PDownloader-browser-ext) over a local HTTP bridge.
- **System tray & background service** — a lightweight background service coordinates the main UI, the download engine window, and the tray icon over local IPC.

---

## Architecture

PDownloader is split into several cooperating processes that communicate over a custom local IPC layer (**CFS**, `PDownloader.CFS`) and a local HTTP bridge used by the browser extension.

```
Browser
  Companion extension (separate repository)
        │  HTTP POST http://localhost:6287
        ▼
PDownloader.Core  (background service / process owner)
  • HTTP bridge on :6287 (/ping, /download, /youtube/analyze, /youtube/download)
  • CFS coordinator — routes commands between processes
  • Owns the download lifecycle through PDownloader.Downloads
        │
        ├── CFS ──▶ PDownloader        (main WPF UI: settings, app entry point)
        ├── CFS ──▶ PDownloader.Tray   (system tray icon, navigation events)
        └── CFS ──▶ PDownloader.Runner (download progress/control UI)

PDownloader.Downloads
  • DownloadManager / DownloadEngine / HLS / segments / recovery orchestration
        │
        ▼
PDownloader.Infrastructure
  • HTTP/IO adapters, hashing/merge recovery, yt-dlp and ffmpeg integration
        │
        ▼
PDownloader.Contracts
  • Shared DTOs, enums and CFS protocol constants
```

### Projects

| Project | Role |
|---|---|
| `PDownloader` | Main WPF application: startup, app configuration, settings UI. Sends download commands to Core over CFS. |
| `PDownloader.Core` | Background service and process owner. Hosts the HTTP bridge, coordinates CFS, owns update orchestration, and composes the download module. |
| `PDownloader.Downloads` | Download application/domain module: manager, engine, HLS/segment orchestration, resume/retry and recovery state. |
| `PDownloader.Infrastructure` | Concrete download adapters: HTTP/IO, hashing and merge recovery, external-process integration, yt-dlp and ffmpeg. |
| `PDownloader.Contracts` | UI-free shared DTOs, enums, update contracts and download protocol constants used across process boundaries. |
| `PDownloader.Runner` | WPF progress/control client for an individual download. The actual transfer remains owned by Core/Downloads. |
| `PDownloader.Tray` | System tray icon that forwards navigation and update events to Core. |
| `PDownloader.CFS` | Transport-only local IPC library used by the desktop processes. |
| `PDownloader.Installer` | Windows installer/setup application. |
| `PDownloader.BugTracker` | Companion crash-reporting window launched on unhandled exceptions. |
| `WPF-UI.LIB` | Forked/customized WPF-UI controls used across the desktop apps. |

---

## Download Engine

Located in `PDownloader.Downloads/DownloadEngine.cs`, the engine works roughly like this:

1. **Probe** the target URL (`HEAD`, falling back to a ranged `GET`) to determine total size and whether the server supports `Accept-Ranges: bytes`.
2. **Split into segments** — when range requests are supported, the file is divided across multiple parallel byte-range downloads.
3. **Write to temp files** — each segment is written to its own `.part` file under a per-download temp folder.
4. **Persist state** — segment progress is saved to disk so a download can resume after the app is closed or crashes.
5. **Retry with back-off** — failed segments are retried with exponential delay; a server that rejects `Range` requests (HTTP 403) triggers an automatic fallback to a non-range retry for that segment.
6. **HLS playlists** — `.m3u8` playlists are detected and their fragments downloaded concurrently under a bounded semaphore, then merged.
7. **Merge & cleanup** — completed segments/fragments are concatenated into the final file and the temp folder is removed.

If a server doesn't support ranged requests at all, the engine transparently falls back to a single-stream download.

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

Run `build.bat` from the repository root to publish the Windows projects and
produce the release installer at `installer-output/PDownloader.Installer.exe`.

### Silent installer

Use `--silent` (or `--quiet`, `-s`, `/S`) to install without displaying the
installer window or asking for input:

```powershell
PDownloader.Installer.exe --silent
```

On the first run, silent installation uses the current account by default and installs to
`%LocalAppData%\Programs\PDownloader` without requesting administrator
permission. Use `--all-users` to install to `C:\Program Files\PDownloader`;
that mode requests UAC elevation only when installation begins.

After a successful installation, the installer remembers the selected install
scope, language, Desktop and Start menu shortcuts, browser extension option,
and Windows startup option. Interactive, uninstall, and silent runs load the
same saved preferences. Explicit command-line parameters always override saved
values.

When an existing PDownloader installation is detected, update and repair runs
lock its install scope and folder to the registered values. The corresponding
controls are disabled in the interactive installer, and silent runs ignore
scope or folder changes. Uninstall PDownloader first before selecting a
different scope or install directory.

The silent mode supports these optional parameters:

| Parameter | Effect |
| --- | --- |
| `--just-me` | Installs only for the current account without UAC. This is the default. |
| `--all-users` | Installs for every user and requests administrator permission. |
| `--language en` / `--language vi` | Overrides the remembered installer language. |
| `--install-dir "C:\Apps\PDownloader"` | Sets a custom installation directory. `/DIR=...` is also accepted. |
| `--desktop-shortcut` / `--no-desktop-shortcut` | Enables or disables the Desktop shortcut. |
| `--start-menu-shortcut` / `--no-start-menu-shortcut` | Enables or disables the Start menu shortcut. |
| `--browser-extension` / `--no-browser-extension` | Enables or disables automatic extension installation for supported browsers. |
| `--run-at-startup` / `--no-run-at-startup` | Enables or disables starting PDownloader with Windows. The existing setting is preserved when neither is supplied. |
| `--launch-after-install` | Launches PDownloader after installation. Silent mode does not launch it by default. |
| `--uninstall --silent` | Uninstalls PDownloader without displaying the installer window. |

The process exits with code `0` on success and `1` on failure. UAC is required
only for an all-users installation or uninstallation.

The updater is owned by the continuously running PDownloader Core process.
The Main App and Tray only send commands and display update state received
from Core, so checking and downloading continue even when the Main App is
closed. Startup, state, and update commands use asynchronous CFS pipe calls so
a busy or not-yet-ready Core cannot block the Main App UI until a pipe timeout.
Checks explicitly requested by the Main App update its UI without showing a
Tray balloon. Background checks may notify through Tray, but the same version
is shown at most once per Tray session.
Core launches a downloaded installer with `--silent` and
`--launch-after-install`, and PDownloader is opened again automatically after
a successful update.

The “Automatically download and install updates” setting is disabled by
default and is saved in the shared user settings. When enabled, Core checks
immediately at startup and every 15 minutes thereafter. Any check—background
or explicitly requested by the Main App—that detects an update immediately
downloads it and starts the silent installation; the 15-minute timer is only
the recurring fallback interval. If an installer was downloaded but not
installed, its pending-update marker is consumed only the next time PDownloader
Core starts. The Main App no longer contains pending-install startup logic;
when it starts Core, Core is the component that decides whether to run the
installer.

---

## License

PDownloader is licensed under the **GNU General Public License v3.0**. See [`LICENSE`](./LICENSE) (and [`LICENSE.vi`](./LICENSE.vi) for a Vietnamese translation) for the full text.
