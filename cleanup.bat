@echo off

for /f "delims=" %%i in ('dir /ad /b /s bin 2^>nul') do rd /s /q "%%i"
for /f "delims=" %%i in ('dir /ad /b /s obj 2^>nul') do rd /s /q "%%i"

echo Done.
pause