@echo off
setlocal
cd /d "%~dp0"

where node >nul 2>nul
if errorlevel 1 (
  echo Node.js not found. Install Node.js first, or use the desktop version.
  pause
  exit /b 1
)

rem Browser version of the scenario editor: a small local server plus the page. No Electron, no game needed.
rem Extra arguments are passed on, e.g.  --scenarios "D:\my scenarios"  --port 8777  --host 0.0.0.0
start "" /b cmd /c "ping -n 2 127.0.0.1 >nul & start http://127.0.0.1:8777/scenario.html"
node scenario-server.js --port 8777 %*
if errorlevel 1 pause

endlocal
