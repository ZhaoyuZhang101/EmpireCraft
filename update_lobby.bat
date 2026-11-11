@echo off
setlocal
pushd "%~dp0" || exit /b 1

rem Update both lobby routes by default. Modes: cf, cn, check.
set "SERVER=8.159.129.136"
set "SERVER_USER=root"
set "SERVER_DIR=/opt/empirecraft-lobby"
set "SERVER_PORT=8765"
set "LOBBY_DIR=Tools\TerminalLobby"
set "CF_URL=https://lobby.cmhgamedev.dpdns.org"
set "CN_URL=http://%SERVER%:%SERVER_PORT%"
set "MODE=%~1"
set "RESULT=0"

if not defined MODE goto :update_cf
if /i "%MODE%"=="check" goto :check
if /i "%MODE%"=="cf" goto :update_cf
if /i "%MODE%"=="cn" goto :update_cn
echo Usage: %~nx0 [cf^|cn^|check]
set "RESULT=1"
goto :finish

:update_cf
echo.
echo ===== International lobby - Cloudflare =====
call :deploy_cf
if errorlevel 1 set "RESULT=1"
if /i "%MODE%"=="cf" goto :check

:update_cn
echo.
echo ===== Mainland lobby - Aliyun %SERVER% =====
call :deploy_cn
if errorlevel 1 set "RESULT=1"

:check
echo.
echo ===== Lobby health checks =====
where curl.exe >nul 2>&1
if errorlevel 1 (
    echo curl.exe is required to verify the lobby.
    set "RESULT=1"
    goto :finish
)
if /i not "%MODE%"=="cn" call :health "International lobby" "%CF_URL%"
if /i not "%MODE%"=="cf" call :health "Mainland lobby" "%CN_URL%"

:finish
echo.
if "%RESULT%"=="0" (
    echo Done.
) else (
    echo One or more steps failed. See the messages above.
)
if not defined EC_NOPAUSE pause
popd
exit /b %RESULT%

:deploy_cf
if not exist "%LOBBY_DIR%\wrangler.toml" (
    echo Missing %LOBBY_DIR%\wrangler.toml.
    exit /b 1
)
where npx >nul 2>&1
if errorlevel 1 (
    echo npx was not found. Install Node.js and try again.
    exit /b 1
)
call npx --yes wrangler@3 deploy --config "%LOBBY_DIR%\wrangler.toml"
if errorlevel 1 (
    echo Cloudflare deploy failed. If Wrangler is not authenticated, run npx wrangler@3 login.
    exit /b 1
)
echo Cloudflare deploy completed.
exit /b 0

:deploy_cn
if not exist "%LOBBY_DIR%\install.sh" (
    echo Missing %LOBBY_DIR%\install.sh.
    exit /b 1
)
where ssh.exe >nul 2>&1
if errorlevel 1 (
    echo ssh.exe was not found. Install the Windows OpenSSH Client and try again.
    exit /b 1
)
where tar.exe >nul 2>&1
if errorlevel 1 (
    echo tar.exe was not found. See the manual deployment instructions.
    exit /b 1
)
set "ARCHIVE=%TEMP%\EmpireCraftLobby-%RANDOM%-%RANDOM%.tar"
tar.exe -cf "%ARCHIVE%" --exclude=.wrangler --exclude=node_modules --exclude=*.md -C "%LOBBY_DIR%" .
if errorlevel 1 (
    if exist "%ARCHIVE%" del /q "%ARCHIVE%"
    echo Failed to package the lobby. No files were sent.
    exit /b 1
)
echo SSH may ask for the %SERVER_USER% password. Typing it will not show any characters.
ssh.exe %SERVER_USER%@%SERVER% "mkdir -p %SERVER_DIR% && tar -xf - --no-same-owner -C %SERVER_DIR% && sed -i 's/\r$//' %SERVER_DIR%/install.sh && bash %SERVER_DIR%/install.sh %SERVER_PORT%" < "%ARCHIVE%"
set "SSH_RESULT=%errorlevel%"
del /q "%ARCHIVE%"
if not "%SSH_RESULT%"=="0" (
    echo Aliyun update failed. See the SSH or installation error above.
    exit /b 1
)
echo Aliyun update completed.
exit /b 0

:health
curl.exe -fsS --max-time 20 -A "EmpireCraft-LobbyUpdate/1" "%~2/healthz" | findstr.exe /x /c:"ok" >nul
if errorlevel 1 (
    echo %~1: health check failed at %~2/healthz
    set "RESULT=1"
) else (
    echo %~1: healthy at %~2/
)
exit /b 0
