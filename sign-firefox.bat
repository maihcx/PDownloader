@echo off
setlocal

REM ============================================================
REM sign-firefox.bat
REM Build and submit the Firefox extension to Mozilla AMO for
REM UNLISTED signing (self-distribution).
REM
REM Required environment variables:
REM   WEB_EXT_API_KEY     = AMO JWT issuer
REM   WEB_EXT_API_SECRET  = AMO JWT secret
REM
REM The secret is intentionally NOT stored in this repository.
REM ============================================================

set "ROOT_DIR=%~dp0"
set "EXT_SOURCE_DIR=%ROOT_DIR%BrowserExtension"
set "EXT_DIR=%EXT_SOURCE_DIR%\dist\firefox"
set "ARTIFACTS_DIR=%EXT_SOURCE_DIR%\web-ext-artifacts"

if "%WEB_EXT_API_KEY%"=="" (
    echo [ERROR] WEB_EXT_API_KEY is not set.
    echo.
    echo Open AMO Developer Hub, create API credentials, then run:
    echo   set "WEB_EXT_API_KEY=your-jwt-issuer"
    echo   set "WEB_EXT_API_SECRET=your-jwt-secret"
    echo   sign-firefox.bat
    echo.
    echo Do not commit or share the API secret.
    exit /b 1
)

if "%WEB_EXT_API_SECRET%"=="" (
    echo [ERROR] WEB_EXT_API_SECRET is not set.
    echo Do not put the secret in source code or commit it to Git.
    exit /b 1
)

where node >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Node.js was not found in PATH.
    exit /b 1
)

where npx >nul 2>nul
if errorlevel 1 (
    echo [ERROR] npx was not found in PATH.
    exit /b 1
)

echo Building Firefox extension...
call "%EXT_SOURCE_DIR%\build-extension.bat" firefox
if errorlevel 1 (
    echo [ERROR] Firefox build failed.
    exit /b 1
)

if not exist "%EXT_DIR%\manifest.json" (
    echo [ERROR] Firefox build output was not found: "%EXT_DIR%"
    exit /b 1
)

if not exist "%ARTIFACTS_DIR%" mkdir "%ARTIFACTS_DIR%"

echo.
echo Submitting extension to Mozilla for UNLISTED signing...
echo Extension source: %EXT_DIR%
echo Signed artifacts: %ARTIFACTS_DIR%
echo.

REM web-ext reads WEB_EXT_API_KEY and WEB_EXT_API_SECRET directly.
REM This avoids exposing the API secret in this batch file or Git history.
call npx --yes web-ext sign ^
    --channel=unlisted ^
    --source-dir "%EXT_DIR%" ^
    --artifacts-dir "%ARTIFACTS_DIR%"

if errorlevel 1 (
    echo.
    echo [ERROR] Mozilla signing failed.
    echo Review the web-ext output above. AMO may require automated or manual review.
    exit /b 1
)

echo.
echo Publishing signed XPI to BrowserExtension\PDownloader.xpi...
call "%ROOT_DIR%publish-firefox-xpi.bat"
if errorlevel 1 (
    echo [ERROR] Mozilla signing succeeded, but publishing PDownloader.xpi failed.
    exit /b 1
)

echo.
echo [OK] Mozilla signing and publishing completed.
echo [INFO] Commit and push BrowserExtension\PDownloader.xpi and BrowserExtension\updates.json together.
exit /b 0
