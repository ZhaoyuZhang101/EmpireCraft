@echo off
rem One-click publish: push -> build check -> update the Windows core and Linux server -> publish the mod
rem   publish_all.bat [-NoPush] [-NoServer] [-NoClient] [-Lobby]
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_all.ps1" %*
set "RESULT=%errorlevel%"
if not defined EC_NOPAUSE pause
exit /b %RESULT%
