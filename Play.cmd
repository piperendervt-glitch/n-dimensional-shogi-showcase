@echo off
rem Double-click to start the built game.
setlocal
set "EXE=%~dp0release\NDShogi\NDShogi.exe"
if not exist "%EXE%" (echo No build found. Run Build.cmd first.& pause & exit /b 1)
start "" "%EXE%"
