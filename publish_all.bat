@echo off
rem One-click publish: push -> build check -> update the local Windows core -> publish the mod
rem   publish_all.bat [-NoPush] [-Server] [-NoServer] [-NoClient] [-Lobby]
rem   The Linux server is not deployed unless -Server is given (official server stopped 2026-11-18).
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_all.ps1" %*
set "RESULT=%errorlevel%"
if not defined EC_NOPAUSE pause
exit /b %RESULT%
