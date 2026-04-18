@echo off
setlocal
cd /d "%~dp0"

if not exist "package.json" (
  echo package.json not found.
  pause
  exit /b 1
)

if not exist "node_modules" (
  echo Installing dependencies...
  call npm install
  if errorlevel 1 (
    echo npm install failed.
    pause
    exit /b 1
  )
)

if not exist "node_modules\electron\cli.js" (
  echo Electron runtime not found in node_modules.
  echo Try deleting node_modules and run this file again.
  pause
  exit /b 1
)

call node node_modules\electron\cli.js .
if errorlevel 1 (
  echo Desktop app failed to start.
  pause
  exit /b 1
)

endlocal
