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

if not exist "node_modules\electron-builder\cli.js" (
  echo electron-builder not found in node_modules.
  echo Try deleting node_modules and run this file again.
  pause
  exit /b 1
)

call node node_modules\electron-builder\cli.js --win portable
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

echo Build finished. Output folder: dist
pause
endlocal
