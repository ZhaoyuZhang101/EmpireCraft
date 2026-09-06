@echo off
setlocal
cd /d "%~dp0"

where pwsh >nul 2>nul
if %errorlevel% equ 0 (
    pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
) else (
    powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
)

if errorlevel 1 (
    echo.
    echo Release failed. See the message above.
    pause
    exit /b 1
)

echo.
echo Release completed successfully.
pause
