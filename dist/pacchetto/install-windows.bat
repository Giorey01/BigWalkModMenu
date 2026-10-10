@echo off
rem Big Walk Mod Menu - install on Windows 10 / 11.
rem Double-click to install. If the game is not found, drag its folder onto this file.
setlocal
cd /d "%~dp0"
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-windows.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-windows.ps1" -Game "%~1"
)
echo.
pause
