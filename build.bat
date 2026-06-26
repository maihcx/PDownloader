@echo off
setlocal

set CONFIG=Release
set PLATFORM=x64
set OUT=build\%CONFIG%

echo ========================================
echo  PDownloader — Build Script
echo ========================================

:: Restore
dotnet restore PDownloader.sln
if errorlevel 1 goto :err

:: Build all projects
dotnet build PDownloader.sln -c %CONFIG% -p:Platform=%PLATFORM% --no-restore
if errorlevel 1 goto :err

:: Publish each runnable component to build\Release\
echo.
echo Publishing components...

dotnet publish PDownloader\PDownloader.csproj                 -c %CONFIG% -o %OUT%\PDownloader                --no-restore
dotnet publish PDownloader.Core\PDownloader.Core.csproj       -c %CONFIG% -o %OUT%\PDownloader                --no-restore
dotnet publish PDownloader.Runner\PDownloader.Runner.csproj   -c %CONFIG% -o %OUT%\PDownloader                --no-restore
dotnet publish PDownloader.Tray\PDownloader.Tray.csproj       -c %CONFIG% -o %OUT%\PDownloader                --no-restore
dotnet publish PDownloader.Installer\PDownloader.Installer.csproj -c %CONFIG% -o %OUT%\PDownloader\Installer --no-restore

:: Copy browser extension
if not exist %OUT%\BrowserExtension mkdir %OUT%\BrowserExtension
xcopy /E /I /Y BrowserExtension %OUT%\BrowserExtension

echo.
echo ========================================
echo  Build complete: %OUT%
echo ========================================
goto :eof

:err
echo.
echo [ERROR] Build failed.
exit /b 1
