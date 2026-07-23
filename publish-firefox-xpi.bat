@echo off
setlocal

REM ============================================================
REM publish-firefox-xpi.bat
REM Copy the newest Mozilla-signed XPI from web-ext-artifacts to
REM the stable repository path used by Firefox Enterprise Policy.
REM ============================================================

set "ROOT_DIR=%~dp0"
set "EXT_SOURCE_DIR=%ROOT_DIR%BrowserExtension"
set "ARTIFACTS_DIR=%EXT_SOURCE_DIR%\web-ext-artifacts"
set "PUBLISHED_XPI=%EXT_SOURCE_DIR%\PDownloader.xpi"
set "FIREFOX_CONFIG=%EXT_SOURCE_DIR%\manifests\firefox.json"
set "UPDATE_SCRIPT=%EXT_SOURCE_DIR%\generate-firefox-updates.ps1"
set "UPDATE_MANIFEST=%EXT_SOURCE_DIR%\updates.json"

if not exist "%ARTIFACTS_DIR%" (
    echo [ERROR] Signed artifacts directory was not found:
    echo   %ARTIFACTS_DIR%
    exit /b 1
)

set "SIGNED_XPI="
for /f "delims=" %%F in ('dir /b /a-d /o-d "%ARTIFACTS_DIR%\*.xpi" 2^>nul') do (
    if not defined SIGNED_XPI set "SIGNED_XPI=%ARTIFACTS_DIR%\%%F"
)

if not defined SIGNED_XPI (
    echo [ERROR] No signed XPI was found in:
    echo   %ARTIFACTS_DIR%
    exit /b 1
)

copy /y "%SIGNED_XPI%" "%PUBLISHED_XPI%" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy the signed XPI to:
    echo   %PUBLISHED_XPI%
    exit /b 1
)

echo Generating Firefox update manifest from the signed XPI...
powershell -NoProfile -ExecutionPolicy Bypass -File "%UPDATE_SCRIPT%" ^
    -XpiPath "%PUBLISHED_XPI%" ^
    -ConfigPath "%FIREFOX_CONFIG%" ^
    -OutputPath "%UPDATE_MANIFEST%"
if errorlevel 1 (
    echo [ERROR] XPI was copied, but updates.json could not be generated.
    exit /b 1
)

echo.
echo [OK] Firefox XPI and update manifest published locally.
echo Source:
echo   %SIGNED_XPI%
echo Target XPI:
echo   %PUBLISHED_XPI%
echo Update manifest:
echo   %UPDATE_MANIFEST%
echo.
echo Commit and push BOTH files together:
echo   BrowserExtension\PDownloader.xpi
echo   BrowserExtension\updates.json
echo.
echo Firefox update manifest URL:
echo   https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/updates.json
echo Firefox signed XPI URL:
echo   https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/PDownloader.xpi
exit /b 0
