@echo off
rem Big Walk Mod Menu - installazione su Windows 10 / 11.
rem Doppio clic per installare. Se il gioco non viene trovato, trascina la sua cartella su questo file.
setlocal
cd /d "%~dp0"
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installa-windows.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installa-windows.ps1" -Gioco "%~1"
)
echo.
pause
