@echo off
rem Big Walk Mod Menu - uninstall on Windows 10 / 11.
rem Double-click to uninstall. If the game is not found, drag its folder onto this file.
setlocal
cd /d "%~dp0"
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall-windows.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall-windows.ps1" -Game "%~1"
)
echo.
pause
