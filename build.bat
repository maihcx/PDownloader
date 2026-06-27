@echo off
setlocal enabledelayedexpansion

echo ============================================
echo   Building PDownloader Installer (WPF)
echo ============================================

set APP_PROJECTS=PDownloader PDownloader.Core PDownloader.Runner PDownloader.Tray
set INSTALLER_PROJECT=PDownloader.Installer\PDownloader.Installer.csproj
set OUTPUT_DIR=.\installer-output
set PAYLOAD_DIR=%OUTPUT_DIR%\publish
set PAYLOAD_ZIP=.\PDownloader.Installer\Resources\payload.zip

REM Xóa output cũ
if exist "%OUTPUT_DIR%" (
    echo Cleaning previous output...
    rmdir /s /q "%OUTPUT_DIR%"
)
mkdir "%OUTPUT_DIR%"
mkdir "%PAYLOAD_DIR%"

echo.
echo Starting build process...

REM ── Bước 1: Build các app project vào publish/ ──
for %%P in (%APP_PROJECTS%) do (
    echo [%%P] Building...

    dotnet publish .\%%P\%%P.csproj -c Release -r win-x64 ^
        /p:DebugType=None ^
        /p:DebugSymbols=false ^
        -o "%PAYLOAD_DIR%"

    if !errorlevel! neq 0 (
        echo [%%P] Build FAILED!
        pause
        exit /b !errorlevel!
    )

    echo [%%P] Build successful!
    echo.
)

REM ── Bước 2: Tạo payload.zip rỗng placeholder để pass 1 compile được ──
echo Creating placeholder payload.zip for pass 1...
if exist "%PAYLOAD_ZIP%" del /f /q "%PAYLOAD_ZIP%"
powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression; $ms = New-Object System.IO.MemoryStream; $za = New-Object System.IO.Compression.ZipArchive($ms, 'Create'); $za.Dispose(); [System.IO.File]::WriteAllBytes('%PAYLOAD_ZIP%', $ms.ToArray())"
if !errorlevel! neq 0 (
    echo [ERROR] Failed to create placeholder payload.zip!
    pause
    exit /b !errorlevel!
)

REM ── Bước 3: Build Installer lần 1 → installer-output/ (payload rỗng) ──
echo [PDownloader.Installer] Building (pass 1 - placeholder payload)...

dotnet publish "%INSTALLER_PROJECT%" -c Release -r win-x64 ^
    /p:DebugType=None ^
    /p:DebugSymbols=false ^
    -o "%OUTPUT_DIR%"

if !errorlevel! neq 0 (
    echo [PDownloader.Installer] Pass 1 FAILED!
    pause
    exit /b !errorlevel!
)
echo [PDownloader.Installer] Pass 1 successful!
echo.

REM ── Bước 4: Copy PDownloader.Installer.exe vào publish/ để nhúng vào payload ──
echo Copying PDownloader.Installer.exe into payload...
copy /y "%OUTPUT_DIR%\PDownloader.Installer.exe" "%PAYLOAD_DIR%\PDownloader.Installer.exe" >nul

REM Copy LICENSE vào payload nếu có
if exist "LICENSE" (
    copy /y "LICENSE" "%PAYLOAD_DIR%\LICENSE" >nul
    echo Copied LICENSE into payload
)
if exist "LICENSE.vi" (
    copy /y "LICENSE.vi" "%PAYLOAD_DIR%\LICENSE.vi" >nul
    echo Copied LICENSE.vi into payload
)

REM ── Bước 5: Zip publish/* → payload.zip (ghi đè bản rỗng) ──
echo.
echo Packaging payload into zip...

if exist "%PAYLOAD_ZIP%" del /f /q "%PAYLOAD_ZIP%"

powershell -NoProfile -Command ^
    "Compress-Archive -Path '%PAYLOAD_DIR%\*' -DestinationPath '%PAYLOAD_ZIP%' -Force"

if !errorlevel! neq 0 (
    echo [ERROR] Failed to create payload.zip!
    pause
    exit /b !errorlevel!
)

REM Kiểm tra payload.zip phải tồn tại và có dung lượng hợp lý (>= 1MB)
if not exist "%PAYLOAD_ZIP%" (
    echo [ERROR] payload.zip was not created!
    pause
    exit /b 1
)
for %%F in ("%PAYLOAD_ZIP%") do set ZIP_SIZE=%%~zF
if !ZIP_SIZE! LSS 1048576 (
    echo [ERROR] payload.zip is too small (!ZIP_SIZE! bytes^). Something went wrong!
    pause
    exit /b 1
)
echo Payload zip created: %PAYLOAD_ZIP% (!ZIP_SIZE! bytes^)
echo.

REM ── Bước 6: Rebuild Installer với payload.zip thật → installer-output/ ──
echo [PDownloader.Installer] Building (pass 2 - with payload)...

dotnet publish "%INSTALLER_PROJECT%" -c Release -r win-x64 ^
    /p:DebugType=None ^
    /p:DebugSymbols=false ^
    -o "%OUTPUT_DIR%"

if !errorlevel! neq 0 (
    echo [PDownloader.Installer] Pass 2 FAILED!
    pause
    exit /b !errorlevel!
)
echo [PDownloader.Installer] Pass 2 successful!

REM Kiểm tra output cuối phải lớn hơn payload.zip (vì còn có .NET runtime + app)
for %%F in ("%OUTPUT_DIR%\PDownloader.Installer.exe") do set EXE_SIZE=%%~zF
if !EXE_SIZE! LSS !ZIP_SIZE! (
    echo [ERROR] Output exe (!EXE_SIZE! bytes^) is smaller than payload zip (!ZIP_SIZE! bytes^).
    echo         payload.zip was likely not embedded correctly!
    pause
    exit /b 1
)
echo Output size: !EXE_SIZE! bytes - OK

REM ── Bước 7: Dọn dẹp tất cả file tạm ──
echo.
echo Cleaning up intermediate files...
if exist "%PAYLOAD_DIR%" rmdir /s /q "%PAYLOAD_DIR%"
if exist "%PAYLOAD_ZIP%" del /f /q "%PAYLOAD_ZIP%"

echo.
echo ============================================
echo   Done!
echo   Single-file installer: %OUTPUT_DIR%\PDownloader.Installer.exe
echo   Size: !EXE_SIZE! bytes
echo ============================================
pause
