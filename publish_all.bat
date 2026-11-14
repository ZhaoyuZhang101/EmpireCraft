@echo off
rem One-click publish (see publish_all.ps1): push -> build check -> update the server -> publish the mod update
rem   publish_all.bat [-NoPush] [-NoServer] [-NoClient] [-Lobby]
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_all.ps1" %*
set "RESULT=%errorlevel%"
if not defined EC_NOPAUSE pause
exit /b %RESULT%
