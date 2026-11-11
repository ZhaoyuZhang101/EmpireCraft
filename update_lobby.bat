@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem ===== 联机大厅一键更新：国际线路(Cloudflare) + 国内线路(阿里云) =====
rem 用法：
rem   双击运行               两条线路都更新
rem   update_lobby.bat cf    只更新国际线路
rem   update_lobby.bat cn    只更新国内线路
rem   update_lobby.bat check 只检查两条线路通不通，不更新
rem 国内线路会问一次服务器密码；国际线路头一回要先运行 npx wrangler@3 login 登录。
rem 换了服务器改下面几项(模组里 TerminalHost.Lobbies 和 lobby.html 的 LINES 也要跟着改)。
set "SERVER=8.159.129.136"
set "SERVER_USER=root"
set "SERVER_DIR=/opt/empirecraft-lobby"
set "SERVER_PORT=8765"
set "LOBBY_DIR=Tools\TerminalLobby"
set "CF_URL=https://lobby.cmhgamedev.dpdns.org"
set "MODE=%~1"
set "FAILED="

if not exist "%LOBBY_DIR%\wrangler.toml" (
    echo 找不到 %LOBBY_DIR%\wrangler.toml，这个文件要放在模组根目录里运行。
    set "FAILED=1"
    goto :end
)
if /i "%MODE%"=="check" goto :check
if /i "%MODE%"=="cn" goto :aliyun

echo.
echo ===== 国际线路 · Cloudflare =====
where npx >nul 2>nul
if errorlevel 1 (
    echo 没有找到 npx，请先安装 Node.js。
    set "FAILED=1"
    goto :after_cf
)
call npx wrangler@3 deploy --config "%LOBBY_DIR%\wrangler.toml"
if errorlevel 1 (
    echo.
    echo 国际线路发布失败。没登录过的话先运行： npx wrangler@3 login
    set "FAILED=1"
) else (
    echo 国际线路发布完成。
)
:after_cf
if /i "%MODE%"=="cf" goto :check

:aliyun
echo.
echo ===== 国内线路 · 阿里云 %SERVER% =====
where ssh >nul 2>nul
if errorlevel 1 (
    echo 没有找到 ssh。在 Windows 的可选功能里装上 OpenSSH 客户端再试。
    set "FAILED=1"
    goto :check
)
where tar >nul 2>nul
if errorlevel 1 (
    echo 没有找到 tar，系统太旧了：改照发布说明里的 scp 办法手动传。
    set "FAILED=1"
    goto :check
)
echo 接下来会问一次服务器 %SERVER_USER% 的密码，输入时不显示字符，输完按回车。
tar -cf - --exclude=.wrangler --exclude=node_modules --exclude=*.md -C "%LOBBY_DIR%" . | ssh %SERVER_USER%@%SERVER% "mkdir -p %SERVER_DIR% && tar -xf - --no-same-owner -C %SERVER_DIR% && sed -i 's/\r$//' %SERVER_DIR%/install.sh && bash %SERVER_DIR%/install.sh %SERVER_PORT%"
if errorlevel 1 (
    echo.
    echo 国内线路更新失败，看上面的提示。密码输错、安全组没放行 22 端口都会这样。
    set "FAILED=1"
) else (
    echo 国内线路更新完成。
)

:check
echo.
echo ===== 检查两条线路 =====
where curl >nul 2>nul
if errorlevel 1 (
    echo 没有找到 curl，请用浏览器打开两个大厅网址看看。
    goto :end
)
set "CF_OK="
set "CN_OK="
for /f "delims=" %%i in ('curl -s -m 20 -A "EmpireCraft-LobbyUpdate/1" "%CF_URL%/healthz" 2^>nul') do set "CF_OK=%%i"
for /f "delims=" %%i in ('curl -s -m 20 -A "EmpireCraft-LobbyUpdate/1" "http://%SERVER%:%SERVER_PORT%/healthz" 2^>nul') do set "CN_OK=%%i"
if "%CF_OK%"=="ok" (
    echo 国际线路：正常    %CF_URL%/
) else (
    echo 国际线路：没有回应  %CF_URL%/
    set "FAILED=1"
)
if "%CN_OK%"=="ok" (
    echo 国内线路：正常    http://%SERVER%:%SERVER_PORT%/
) else (
    echo 国内线路：没有回应  http://%SERVER%:%SERVER_PORT%/
    set "FAILED=1"
)

:end
echo.
if defined FAILED (
    echo 有没成的步骤，看上面的提示。
) else (
    echo 全部完成。
)
if not defined EC_NOPAUSE pause
endlocal
