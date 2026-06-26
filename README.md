# PDownloader

> IDM-style multi-threaded download manager for Windows, built with .NET 10 / WPF + MVVM.

---

## Kiến trúc tổng quan

```
┌──────────────────────────────────────────────────────────────────────┐
│                         Browser (Chrome/Edge)                         │
│   Extension popup / context menu / auto-capture                       │
│             │ HTTP POST localhost:6287                                │
└─────────────┼─────────────────────────────────────────────────────────┘
              ▼
┌─────────────────────────┐      CFS pipe      ┌──────────────────────┐
│   PDownloader.Core      │ ◄──────────────── ▶│  PDownloader.exe     │
│  (background service)   │   MainToCore        │  (main WPF UI /      │
│  • HTTP bridge :6287    │   CoreToMain        │   cấu hình / tray)   │
│  • CFS coordinator      │                    └──────────────────────┘
│  • Routes "download"    │      CFS pipe      ┌──────────────────────┐
│    commands             │ ◄──────────────── ▶│  PDownloader.Tray    │
│                         │   TrayToCore        │  (system tray)       │
│                         │   CoreToTray        └──────────────────────┘
│                         │      CFS pipe      ┌──────────────────────┐
│                         │ ──────────────── ▶ │  PDownloader.Runner  │
│                         │   CoreToRunner      │  (download engine)   │
│                         │   RunnerToCore      │  • Multi-thread      │
└─────────────────────────┘                    │  • Resume / retry    │
                                               │  • Merge segments    │
                                               └──────────────────────┘
```

### Thành phần

| Project | Vai trò |
|---|---|
| **PDownloader** | WPF main UI. Khởi động, cấu hình app, settings page. Gửi lệnh `download` tới Core qua CFS. |
| **PDownloader.Core** | Background Windows service. Điều phối toàn bộ: nhận lệnh từ Main/Tray/Extension, relay tới Runner. Chạy HTTP bridge `:6287`. |
| **PDownloader.Runner** | WPF download manager window. Nhận lệnh `download` từ Core, chạy `DownloadEngine` (IDM-style). |
| **PDownloader.Tray** | System tray icon. Gửi navigation events tới Core. |
| **PDownloader.CFS** | Thư viện ConfluxService — IPC giữa các tiến trình. |
| **PDownloader.Installer** | Trình cài đặt (giữ nguyên). |
| **BrowserExtension** | Chrome/Edge MV3 extension — bắt link và POST tới Core HTTP bridge. |

---

## Migration: Overlay → Runner

### Thay đổi file

| Cũ (CircleSearch) | Mới (PDownloader) |
|---|---|
| `PDownloader.Overlay/` | `PDownloader.Runner/` |
| `OverlayWindow.xaml` | `RunnerWindow.xaml` |
| `OverlayLauncherService.cs` | `DownloadLauncherService.cs` |
| `LauncherSettings` (OCR fields) | `AppSettings` (download fields) |
| `AppRuntime.StartOverlay()` | `AppRuntime.StartRunner()` / `SendDownloadToRunner()` |
| `CFSIncomingHandler: start-ocr` | `CFSIncomingHandler: download` |
| `GlobalHotkeyService` | *(removed — extension replaces hotkey capture)* |

### Xóa hoàn toàn
- Tất cả logic OCR, screenshot, CircleSearch
- `PDownloader.Overlay` project (rename + rewrite → `PDownloader.Runner`)
- `OverlayLauncherService`, `LauncherSettings.SearchEngine`, `OcrLanguage`, `OverlayOpacity`

---

## Download Engine (DownloadEngine.cs)

Giống IDM:

1. **HEAD** → lấy `Content-Length` và `Accept-Ranges: bytes`
2. **Chia segment**: N luồng song song (mặc định 8), mỗi luồng tải một byte-range
3. **Temp files**: mỗi segment ghi vào `seg_N.part` trong `%LOCALAPPDATA%\SM SOFT\PDownloader\Temp\<id>\`
4. **State persistence**: `segments.pdstate` (JSON) → resume khi tắt ngang
5. **Auto-retry**: exponential back-off, tối đa 5 lần/segment
6. **Merge**: ghép `seg_0.part … seg_N.part` vào file cuối cùng
7. **Cleanup**: xóa thư mục temp sau khi merge

Nếu server không hỗ trợ `Accept-Ranges` → fallback về single-stream.

---

## Browser Extension

### Cài đặt (Development)
1. Chrome: `chrome://extensions` → **Load unpacked** → chọn thư mục `BrowserExtension/`
2. Edge: `edge://extensions` → **Load unpacked`

### Luồng gửi link
```
User click context menu / popup "Tải xuống"
  → background.js: POST http://localhost:6287/download { url, saveTo, fileName }
  → Core HttpBridgeService: parse → AppRuntime.SendDownloadToRunner(json)
  → cfsRunner.Send("download", json)
  → Runner RunnerCommandHandler.Handle("download", json)
  → RunnerWindow.EnqueueDownload(url, saveTo, fileName)
  → Dialog hiện lên → user xác nhận → DownloadManager.Enqueue(...)
```

### Tính năng extension
- **Context menu**: chuột phải link/video/trang → tải bằng PDownloader
- **Popup**: nhập URL thủ công, chọn thư mục, xem link trên trang
- **Auto-capture**: tự động bắt click vào link `.zip/.exe/.mp4/...`
- **Thông báo**: notify khi thêm vào hàng chờ thành công / lỗi kết nối

---

## CFS Command Reference

### Main → Core
| Name | Value | Mô tả |
|---|---|---|
| `download` | `{ url, saveTo, fileName }` JSON | Yêu cầu tải xuống mới |
| `show-runner` | `""` | Hiện Runner window |
| `tray-event` | event name | Forward tới Main |
| `core-svc-state` | `"shutdown"` | Tắt toàn bộ |

### Core → Runner
| Name | Value | Mô tả |
|---|---|---|
| `download` | `{ url, saveTo, fileName }` JSON | Runner thêm vào hàng chờ |
| `state` | `"shutdown"` | Runner thoát |

---

## Build & Run

```bash
# Build tất cả
dotnet build PDownloader.sln -c Release

# Chạy development
dotnet run --project PDownloader   # Main UI (khởi động Core tự động)
```

**Yêu cầu**: .NET 10 SDK, Windows 10+, x64.
