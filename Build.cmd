@echo off
rem Double-click to build the game into release\NDShogiPivot (takes several minutes).
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\build-game.ps1"
if errorlevel 1 (echo Build failed. See the newest log in release\logs.& pause & exit /b 1)
echo Build finished. Start the game with Play.cmd or release\NDShogiPivot\NDShogiPivot.exe.
pause
