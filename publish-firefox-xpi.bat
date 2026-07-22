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

echo [OK] Firefox XPI published.
echo Source:
echo   %SIGNED_XPI%
echo Target:
echo   %PUBLISHED_XPI%
echo.
echo Commit and push BrowserExtension\PDownloader.xpi so Firefox can install/update it from:
echo   https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/PDownloader.xpi
exit /b 0
