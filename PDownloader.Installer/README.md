# PDownloader Installer

Trình cài đặt WPF cho ứng dụng **PDownloader**, hỗ trợ đa ngôn ngữ (Tiếng Việt / English) và tích hợp gỡ cài đặt qua Control Panel / Windows Settings.

---

## Cấu trúc project

```
PDownloader.Installer/
├── App.xaml / App.xaml.cs         - Application entry point, phát hiện --uninstall flag
├── app.manifest                   - Yêu cầu quyền Administrator
├── build.bat                      - Script build & đóng gói
│
├── Assets/
│   ├── app.ico                    - Icon ứng dụng
│   └── app-256.png                - Logo hiển thị trong sidebar
│
├── Resources/
│   └── Locales/
│       ├── String.resx            - Neutral (fallback keys)
│       ├── String.en.resx         - English
│       ├── String.vi.resx         - Tiếng Việt
│       └── String.Designer.cs     - Auto-generated accessor
│
├── Utils/
│   ├── Translation.cs             - TranslationSource, LocalizationExtension, LanguageBase
│   └── StepConverters.cs          - IValueConverter cho sidebar step indicator
│
├── ViewModels/
│   └── InstallerViewModel.cs      - Toàn bộ logic điều hướng các bước
│
├── Views/
│   ├── MainWindow.xaml            - UI đa bước (step-by-step)
│   └── MainWindow.xaml.cs         - Code-behind
│
└── Services/
    └── InstallService.cs          - Logic cài đặt / gỡ cài đặt thực tế
```

---

## Yêu cầu

- **.NET 8 SDK** trở lên  
- **Windows 10 / 11** (x64)
- Chạy với quyền **Administrator** (do `app.manifest` yêu cầu)

---

## Build

### 1. Chuẩn bị

Đặt project này ngang với thư mục gốc của PDownloader:

```
PDownloader/           ← project gốc
  publish/              ← output sau khi chạy build.bat gốc
    PDownloader.exe
    PDownloader Tray.exe
    PDownloader Core.exe
    PDownloader Overlay.exe
  build.bat             ← build gốc
PDownloader.Installer/ ← project này
  build.bat             ← build installer
```

### 2. Build PDownloader trước

```bat
cd PDownloader
build.bat
```

### 3. Build Installer

```bat
cd PDownloader.Installer
build.bat
```

Output sẽ nằm ở `installer-output\PDownloader.Installer.exe` — **một file exe duy nhất**, self-contained.

---

## Cách hoạt động

### Cài đặt

1. User chạy `PDownloader.Installer.exe`
2. **Chọn ngôn ngữ** (VI / EN) → giao diện chuyển ngôn ngữ ngay lập tức
3. **Màn hình chào mừng**
4. **License Agreement** – phải tick Accept
5. **Chọn thư mục cài đặt** (mặc định: `%ProgramFiles%\PDownloader`)
6. **Tùy chọn** – shortcut desktop, Start Menu, khởi động cùng Windows
7. **Cài đặt** – copy files, tạo shortcut, ghi registry uninstaller
8. **Hoàn tất** – tùy chọn khởi động ứng dụng ngay

### Gỡ cài đặt

Có 2 cách:

**Cách 1:** Qua **Control Panel → Programs → Uninstall a program** → chọn PDownloader → Uninstall  
**Cách 2:** Qua **Windows Settings → Apps → Installed Apps** → PDownloader → Uninstall

Cả hai đều gọi:
```
PDownloader.Installer.exe --uninstall
```

Trình gỡ cài đặt sẽ:
1. Cho user chọn ngôn ngữ
2. Xác nhận gỡ cài đặt
3. Kill process PDownloader đang chạy
4. Xóa thư mục cài đặt
5. Xóa shortcuts (Desktop, Start Menu)
6. Xóa registry key startup
7. Xóa registry key uninstaller

---

## Đa ngôn ngữ (i18n)

Sử dụng cùng kiến trúc với `PDownloader.Tray`:

- **`TranslationSource`** – `INotifyPropertyChanged` singleton, binding-friendly
- **`LocalizationExtension`** – Markup Extension dùng trong XAML: `{local:LocalizationExtension key}`
- Ngôn ngữ được chọn **ngay màn hình đầu tiên** trước khi bắt đầu cài đặt/gỡ cài đặt
- Thêm ngôn ngữ mới: thêm `String.xx.resx` và thêm vào `LanguageBase.SupportedLanguages`

---

## Registry

Installer ghi vào:
```
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PDownloader
```

Với các giá trị:
| Key | Value |
|-----|-------|
| DisplayName | PDownloader |
| DisplayVersion | 1.0.0 |
| Publisher | PDownloader |
| InstallLocation | `<thư mục cài đặt>` |
| DisplayIcon | `<path>\PDownloader.exe` |
| UninstallString | `"<path>\PDownloader.Installer.exe" --uninstall` |
| QuietUninstallString | `"<path>\PDownloader.Installer.exe" --uninstall --quiet` |
| NoModify | 1 |
| NoRepair | 1 |
| EstimatedSize | 51200 (KB) |
