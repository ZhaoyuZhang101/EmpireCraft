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

rem pack-portable.js builds the server core, copies the data the core reads, then runs electron-builder,
rem and draws one progress bar for the whole thing. The packaged editor carries the core and its data
rem (see "extraResources" in package.json), so it runs the core on a PC without the game or the mod.
rem UTF-8 console: the script prints Chinese.
chcp 65001 >nul
call node pack-portable.js
if errorlevel 1 (
  pause
  exit /b 1
)

pause
endlocal
