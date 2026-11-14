@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1"
set code=%errorlevel%
if not "%code%"=="0" echo Publish failed, exit code %code%
pause
exit /b %code%
