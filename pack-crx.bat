@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM pack-crx.bat
REM Dong goi lai BrowserExtension\ thanh PDownloader.crx bang
REM chinh private key da dung tu truoc (signing-key.pem, dat
REM CUNG CAP voi thu muc BrowserExtension, khong nam ben trong no).
REM
REM Chay o REPO ROOT (thu muc chua BrowserExtension\ va signing-key.pem):
REM   pack-crx.bat
REM ============================================================

set EXT_DIR=%~dp0BrowserExtension
set KEY_FILE=%~dp0signing-key.pem
set OUT_CRX=%EXT_DIR%\PDownloader.crx
set TMP_CRX=%~dp0BrowserExtension.crx

REM --- Tim chrome.exe ---
set CHROME_EXE=
if exist "%ProgramFiles%\Google\Chrome\Application\chrome.exe" set CHROME_EXE=%ProgramFiles%\Google\Chrome\Application\chrome.exe
if exist "%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe" set CHROME_EXE=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe
if exist "%LocalAppData%\Google\Chrome\Application\chrome.exe" set CHROME_EXE=%LocalAppData%\Google\Chrome\Application\chrome.exe

if "%CHROME_EXE%"=="" (
    echo [ERROR] Khong tim thay chrome.exe. Sua bien CHROME_EXE trong script nay cho dung duong dan.
    pause
    exit /b 1
)

if not exist "%KEY_FILE%" (
    echo [ERROR] Khong tim thay %KEY_FILE%
    pause
    exit /b 1
)

if not exist "%EXT_DIR%" (
    echo [ERROR] Khong tim thay thu muc %EXT_DIR%
    pause
    exit /b 1
)

REM --- 1) Xoa .crx cu ---
echo Deleting old PDownloader.crx...
if exist "%OUT_CRX%" del /f /q "%OUT_CRX%"
if exist "%TMP_CRX%" del /f /q "%TMP_CRX%"

REM --- 2) Dong goi crx voi signing-key.pem ---
echo Packing extension with %KEY_FILE% ...
"%CHROME_EXE%" --pack-extension="%EXT_DIR%" --pack-extension-key="%KEY_FILE%"

REM Chrome can mat mot chut thoi gian de ghi file va thoat.
timeout /t 2 /nobreak >nul

if not exist "%TMP_CRX%" (
    echo [ERROR] Dong goi that bai - khong thay %TMP_CRX%.
    echo         Kiem tra Chrome da dong het chua truoc khi chay lai.
    pause
    exit /b 1
)

REM --- 3) Copy crx vua dong goi vao trong BrowserExtension ---
echo Copying packed crx into BrowserExtension...
move /y "%TMP_CRX%" "%OUT_CRX%" >nul

if not exist "%OUT_CRX%" (
    echo [ERROR] Copy that bai - khong thay %OUT_CRX%.
    pause
    exit /b 1
)

echo.
echo Done: %OUT_CRX%
echo Nho: kiem tra manifest.json / update.xml da bump version truoc khi dong goi
pause