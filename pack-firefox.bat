@echo off
setlocal

REM ============================================================
REM pack-firefox.bat
REM Build Firefox extension and create an unsigned XPI package.
REM ZIP entries are created with '/' paths so Gecko can resolve
REM resources such as _locales/en/messages.json correctly.
REM ============================================================

set "ROOT_DIR=%~dp0"
set "EXT_SOURCE_DIR=%ROOT_DIR%BrowserExtension"
set "EXT_DIR=%EXT_SOURCE_DIR%\dist\firefox"
set "OUT_XPI=%EXT_SOURCE_DIR%\PDownloader-Firefox-unsigned.xpi"
set "PACK_SCRIPT=%ROOT_DIR%pack-firefox.ps1"

echo Building Firefox extension...
call "%EXT_SOURCE_DIR%\build-extension.bat" firefox
if errorlevel 1 (
    echo [ERROR] Build Firefox extension failed.
    pause
    exit /b 1
)

if not exist "%EXT_DIR%\manifest.json" (
    echo [ERROR] Firefox manifest was not found in "%EXT_DIR%".
    pause
    exit /b 1
)

if not exist "%EXT_DIR%\_locales\en\messages.json" (
    echo [ERROR] Missing Firefox locale: _locales\en\messages.json
    pause
    exit /b 1
)

if not exist "%EXT_DIR%\_locales\vi\messages.json" (
    echo [ERROR] Missing Firefox locale: _locales\vi\messages.json
    pause
    exit /b 1
)

if not exist "%PACK_SCRIPT%" (
    echo [ERROR] Missing packaging script: "%PACK_SCRIPT%"
    pause
    exit /b 1
)

if exist "%OUT_XPI%" del /f /q "%OUT_XPI%"

echo Creating Firefox XPI with normalized archive paths...
powershell -NoProfile -ExecutionPolicy Bypass -File "%PACK_SCRIPT%" ^
    -SourceDir "%EXT_DIR%" ^
    -OutputXpi "%OUT_XPI%"
if errorlevel 1 (
    echo [ERROR] Could not create a valid Firefox XPI.
    pause
    exit /b 1
)

if not exist "%OUT_XPI%" (
    echo [ERROR] Output XPI was not created: "%OUT_XPI%"
    pause
    exit /b 1
)

echo.
echo Done: %OUT_XPI%
echo.
echo Development test ^(recommended^):
echo   about:debugging ^> This Firefox ^> Load Temporary Add-on

echo   Select: %EXT_DIR%\manifest.json

echo.
echo Package test:
echo   You can also select the generated unsigned XPI above.
pause
