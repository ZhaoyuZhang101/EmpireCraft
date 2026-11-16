@echo off
setlocal
cd /d "%~dp0"
rem Publish an editor update: build the installer, upload it to the update service, publish the manifest.
rem   [-NoBuild] [-Notes "text"] [-Yes]   (see publish-editor.ps1)
chcp 65001 >nul
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-editor.ps1" %*
set "RESULT=%errorlevel%"
if not defined EC_NOPAUSE pause
exit /b %RESULT%
