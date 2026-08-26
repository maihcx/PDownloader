@echo off
setlocal enabledelayedexpansion

REM Always build from the directory that contains this script.
cd /d "%~dp0"

set "APP_PROJECTS=PDownloader PDownloader.BugTracker PDownloader.Core PDownloader.Runner PDownloader.Tray"
set "INSTALLER_PROJECT=PDownloader.Installer\PDownloader.Installer.csproj"
set "OUTPUT_ROOT=.\installer-output"
set "PAYLOAD_ZIP=.\PDownloader.Installer\Resources\payload.zip"
set "INSTALLER_BIN=.\PDownloader.Installer\bin"
set "INSTALLER_OBJ=.\PDownloader.Installer\obj"

set "REQUESTED_ARCH=%~1"
if not defined REQUESTED_ARCH set "REQUESTED_ARCH=all"

if /I "%REQUESTED_ARCH%"=="-h" goto :ShowUsage
if /I "%REQUESTED_ARCH%"=="--help" goto :ShowUsage
if /I "%REQUESTED_ARCH%"=="/?" goto :ShowUsage

if /I "%REQUESTED_ARCH%"=="win-x64" set "REQUESTED_ARCH=x64"
if /I "%REQUESTED_ARCH%"=="win-arm64" set "REQUESTED_ARCH=arm64"

if /I "%REQUESTED_ARCH%"=="all" goto :BuildAll
if /I "%REQUESTED_ARCH%"=="x64" goto :BuildOne
if /I "%REQUESTED_ARCH%"=="arm64" goto :BuildOne

echo [ERROR] Unsupported architecture: %REQUESTED_ARCH%
echo.
goto :ShowUsageError

:BuildAll
REM A full build starts from a clean output folder and produces both installers.
if exist "%OUTPUT_ROOT%" (
    echo Cleaning previous output...
    rmdir /s /q "%OUTPUT_ROOT%"
    if exist "%OUTPUT_ROOT%" (
        echo [ERROR] Could not remove %OUTPUT_ROOT%.
        echo         Close every running PDownloader.Installer process, then retry.
        goto :BuildFailed
    )
)

call :BuildArchitecture x64
if errorlevel 1 goto :BuildFailed

call :BuildArchitecture arm64
if errorlevel 1 goto :BuildFailed

goto :BuildComplete

:BuildOne
call :BuildArchitecture "%REQUESTED_ARCH%"
if errorlevel 1 goto :BuildFailed
goto :BuildComplete

:BuildArchitecture
set "TARGET_ARCH=%~1"

if /I "%TARGET_ARCH%"=="x64" (
    set "TARGET_RID=win-x64"
    set "PLATFORM_TARGET=x64"
) else if /I "%TARGET_ARCH%"=="arm64" (
    set "TARGET_RID=win-arm64"
    set "PLATFORM_TARGET=ARM64"
) else (
    echo [ERROR] Internal unsupported architecture: %TARGET_ARCH%
    exit /b 1
)

set "ARCH_OUTPUT_DIR=%OUTPUT_ROOT%\%TARGET_RID%"
set "PAYLOAD_DIR=%ARCH_OUTPUT_DIR%\publish"
set "FINAL_INSTALLER=%OUTPUT_ROOT%\PDownloader.Installer-%TARGET_RID%.exe"

echo.
echo ============================================
echo   Building PDownloader Installer (WPF)
echo   Runtime : %TARGET_RID%
echo   Platform: %PLATFORM_TARGET%
echo ============================================

if not exist "%OUTPUT_ROOT%" mkdir "%OUTPUT_ROOT%"

if exist "%ARCH_OUTPUT_DIR%" (
    echo Cleaning previous %TARGET_RID% intermediate output...
    rmdir /s /q "%ARCH_OUTPUT_DIR%"
)
if exist "%FINAL_INSTALLER%" del /f /q "%FINAL_INSTALLER%"

if exist "%ARCH_OUTPUT_DIR%" (
    echo [ERROR] Could not clean %ARCH_OUTPUT_DIR%.
    exit /b 1
)

REM Force WPF to regenerate architecture-specific compiled XAML/BAML.
if exist "%INSTALLER_BIN%" rmdir /s /q "%INSTALLER_BIN%"
if exist "%INSTALLER_OBJ%" rmdir /s /q "%INSTALLER_OBJ%"

if exist "%INSTALLER_BIN%" (
    echo [ERROR] Could not clean %INSTALLER_BIN%.
    exit /b 1
)
if exist "%INSTALLER_OBJ%" (
    echo [ERROR] Could not clean %INSTALLER_OBJ%.
    exit /b 1
)

mkdir "%ARCH_OUTPUT_DIR%"
mkdir "%PAYLOAD_DIR%"

echo.
echo Starting %TARGET_RID% build process...

REM Step 1: Publish every application for the selected architecture.
for %%P in (%APP_PROJECTS%) do (
    echo [%%P] Building for !TARGET_RID!...

    dotnet publish .\%%P\%%P.csproj -c Release -r !TARGET_RID! ^
        /p:PlatformTarget=!PLATFORM_TARGET! ^
        /p:PublishReadyToRun=true ^
        /p:PublishReadyToRunShowWarnings=true ^
        /p:DebugType=None ^
        /p:DebugSymbols=false ^
        -o "!PAYLOAD_DIR!"

    if !errorlevel! neq 0 (
        set "STEP_EXIT_CODE=!errorlevel!"
        echo [%%P] Build FAILED for !TARGET_RID!!
        call :CleanupArchitecture
        exit /b !STEP_EXIT_CODE!
    )

    echo [%%P] Build successful.
    echo.
)

REM Step 2: Create an empty embedded payload for the first installer pass.
echo Creating placeholder payload.zip...
if exist "%PAYLOAD_ZIP%" del /f /q "%PAYLOAD_ZIP%"
powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression; $ms = New-Object System.IO.MemoryStream; $za = New-Object System.IO.Compression.ZipArchive($ms, 'Create'); $za.Dispose(); [System.IO.File]::WriteAllBytes('%PAYLOAD_ZIP%', $ms.ToArray())"
if errorlevel 1 (
    set "STEP_EXIT_CODE=!errorlevel!"
    echo [ERROR] Failed to create placeholder payload.zip.
    call :CleanupArchitecture
    exit /b !STEP_EXIT_CODE!
)

REM Step 3: Build the installer once with the placeholder payload.
echo [PDownloader.Installer] Building pass 1 for %TARGET_RID%...
dotnet publish "%INSTALLER_PROJECT%" -c Release -r %TARGET_RID% ^
    /p:PlatformTarget=%PLATFORM_TARGET% ^
    /p:PublishReadyToRun=true ^
    /p:PublishReadyToRunShowWarnings=true ^
    /p:DebugType=None ^
    /p:DebugSymbols=false ^
    -o "%ARCH_OUTPUT_DIR%"

if errorlevel 1 (
    set "STEP_EXIT_CODE=!errorlevel!"
    echo [PDownloader.Installer] Pass 1 FAILED for %TARGET_RID%.
    call :CleanupArchitecture
    exit /b !STEP_EXIT_CODE!
)

REM Step 4: Embed an installer of the same architecture for uninstall/repair.
echo Copying %TARGET_RID% installer into payload...
copy /y "%ARCH_OUTPUT_DIR%\PDownloader.Installer.exe" "%PAYLOAD_DIR%\PDownloader.Installer.exe" >nul
if errorlevel 1 (
    set "STEP_EXIT_CODE=!errorlevel!"
    echo [ERROR] Failed to copy installer into payload.
    call :CleanupArchitecture
    exit /b !STEP_EXIT_CODE!
)

if exist "LICENSE" copy /y "LICENSE" "%PAYLOAD_DIR%\LICENSE" >nul
if exist "LICENSE.vi" copy /y "LICENSE.vi" "%PAYLOAD_DIR%\LICENSE.vi" >nul

REM Step 5: Replace the placeholder with the real architecture-specific payload.
echo Packaging %TARGET_RID% payload...
if exist "%PAYLOAD_ZIP%" del /f /q "%PAYLOAD_ZIP%"
powershell -NoProfile -Command ^
    "Compress-Archive -Path '%PAYLOAD_DIR%\*' -DestinationPath '%PAYLOAD_ZIP%' -Force"

if errorlevel 1 (
    set "STEP_EXIT_CODE=!errorlevel!"
    echo [ERROR] Failed to create payload.zip.
    call :CleanupArchitecture
    exit /b !STEP_EXIT_CODE!
)

if not exist "%PAYLOAD_ZIP%" (
    echo [ERROR] payload.zip was not created.
    call :CleanupArchitecture
    exit /b 1
)

for %%F in ("%PAYLOAD_ZIP%") do set "ZIP_SIZE=%%~zF"
if !ZIP_SIZE! LSS 1048576 (
    echo [ERROR] payload.zip is too small ^(!ZIP_SIZE! bytes^).
    call :CleanupArchitecture
    exit /b 1
)

REM Step 6: Rebuild the final installer with the real payload.
echo [PDownloader.Installer] Building pass 2 for %TARGET_RID%...
dotnet publish "%INSTALLER_PROJECT%" -c Release -r %TARGET_RID% ^
    /p:PlatformTarget=%PLATFORM_TARGET% ^
    /p:PublishReadyToRun=true ^
    /p:PublishReadyToRunShowWarnings=true ^
    /p:DebugType=None ^
    /p:DebugSymbols=false ^
    -o "%ARCH_OUTPUT_DIR%"

if errorlevel 1 (
    set "STEP_EXIT_CODE=!errorlevel!"
    echo [PDownloader.Installer] Pass 2 FAILED for %TARGET_RID%.
    call :CleanupArchitecture
    exit /b !STEP_EXIT_CODE!
)

if not exist "%ARCH_OUTPUT_DIR%\PDownloader.Installer.exe" (
    echo [ERROR] Final installer was not created for %TARGET_RID%.
    call :CleanupArchitecture
    exit /b 1
)

for %%F in ("%ARCH_OUTPUT_DIR%\PDownloader.Installer.exe") do set "EXE_SIZE=%%~zF"
if !EXE_SIZE! LSS !ZIP_SIZE! (
    echo [ERROR] Installer ^(!EXE_SIZE! bytes^) is smaller than payload ^(!ZIP_SIZE! bytes^).
    call :CleanupArchitecture
    exit /b 1
)

copy /y "%ARCH_OUTPUT_DIR%\PDownloader.Installer.exe" "%FINAL_INSTALLER%" >nul
if errorlevel 1 (
    set "STEP_EXIT_CODE=!errorlevel!"
    echo [ERROR] Failed to create %FINAL_INSTALLER%.
    call :CleanupArchitecture
    exit /b !STEP_EXIT_CODE!
)

call :CleanupArchitecture

echo.
echo [%TARGET_RID%] Build successful.
echo [%TARGET_RID%] Installer: %FINAL_INSTALLER%
echo [%TARGET_RID%] Size: %EXE_SIZE% bytes
exit /b 0

:CleanupArchitecture
if defined ARCH_OUTPUT_DIR if exist "%ARCH_OUTPUT_DIR%" rmdir /s /q "%ARCH_OUTPUT_DIR%"
if exist "%PAYLOAD_ZIP%" del /f /q "%PAYLOAD_ZIP%"
exit /b 0

:BuildComplete
echo.
echo ============================================
echo   Done!
echo ============================================
echo Output files:
for %%F in ("%OUTPUT_ROOT%\PDownloader.Installer-win-*.exe") do (
    if exist "%%~fF" echo   %%~fF
)
echo.
echo Usage:
echo   build.bat          ^& rem Build both x64 and ARM64 (default)
echo   build.bat x64     ^& rem Build x64
echo   build.bat arm64   ^& rem Build native Windows 11 ARM64
echo   build.bat all     ^& rem Build both x64 and ARM64
pause
exit /b 0

:BuildFailed
set "BUILD_EXIT_CODE=%errorlevel%"
if "%BUILD_EXIT_CODE%"=="0" set "BUILD_EXIT_CODE=1"
echo.
echo ============================================
echo   Build FAILED with exit code %BUILD_EXIT_CODE%
echo ============================================
pause
exit /b %BUILD_EXIT_CODE%

:ShowUsageError
call :ShowUsage
exit /b 1

:ShowUsage
echo Usage:
echo   build.bat          ^& rem Build both x64 and ARM64 (default)
echo   build.bat x64     ^& rem Build x64
echo   build.bat arm64   ^& rem Build native Windows 11 ARM64
echo   build.bat all     ^& rem Build both x64 and ARM64
exit /b 0
