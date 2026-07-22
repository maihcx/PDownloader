@echo off
setlocal

set SCRIPT_DIR=%~dp0
pushd "%SCRIPT_DIR%"

where node >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Khong tim thay Node.js trong PATH.
    popd
    exit /b 1
)

set TARGET=%~1
if "%TARGET%"=="" set TARGET=all

node build-extension.mjs %TARGET%
set EXIT_CODE=%ERRORLEVEL%

if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Build BrowserExtension that bai.
    popd
    exit /b %EXIT_CODE%
)

echo.
echo Chromium: %SCRIPT_DIR%dist\chromium
echo Firefox : %SCRIPT_DIR%dist\firefox

popd
endlocal
