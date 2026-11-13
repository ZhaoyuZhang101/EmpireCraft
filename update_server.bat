@echo off
setlocal EnableDelayedExpansion
pushd "%~dp0" || exit /b 1

rem Build the standalone core locally, upload it to the Linux server and restart it there as a service.
rem   update_server.bat          build + upload + restart
rem   update_server.bat check    only ask the server whether the core is answering
rem   update_server.bat nobuild  upload the build that is already in .server\publish\linux-x64
rem The server keeps its own state folder (server.json, accounts, uploaded scenarios); it is never overwritten.
rem No password prompt if an SSH key is installed on the server (see Tools\StandaloneServer\README.md).
set "SERVER=8.159.129.136"
set "SERVER_USER=root"
set "SERVER_DIR=/opt/empirecraft"
rem Port to check from this PC afterwards (the core's "port" in server.json). Leave empty to skip the outside check.
set "CORE_PORT=8766"
rem Data folders the core reads from the mod folder (source .cs files are left out).
set "DATA_DIRS=Terminal WorldMaps Locales Technology InstitutionTrees Scripts"
set "ROOT=%~dp0"
set "PUBLISH=%ROOT%.server\publish\linux-x64"
set "MODE=%~1"
set "RESULT=0"

if /i "%MODE%"=="check" goto :check
if defined MODE if /i not "%MODE%"=="nobuild" (
    echo Usage: %~nx0 [check^|nobuild]
    set "RESULT=1"
    goto :finish
)

where ssh.exe >nul 2>&1
if errorlevel 1 (
    echo ssh.exe was not found. Install the Windows OpenSSH Client and try again.
    set "RESULT=1"
    goto :finish
)
where tar.exe >nul 2>&1
if errorlevel 1 (
    echo tar.exe was not found.
    set "RESULT=1"
    goto :finish
)

if /i "%MODE%"=="nobuild" goto :package
echo.
echo ===== Build (linux-x64, self-contained) =====
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%.server\publish.ps1" -Runtime linux-x64
if errorlevel 1 (
    echo Build failed. Nothing was sent.
    set "RESULT=1"
    goto :finish
)

:package
if not exist "%PUBLISH%\EmpireCraftServer" (
    echo Missing %PUBLISH%\EmpireCraftServer. Run without "nobuild" first.
    set "RESULT=1"
    goto :finish
)
echo.
echo ===== Package =====
set "JSONS="
for %%F in ("%ROOT%*.json") do (
    if /i not "%%~nxF"=="debug_probe.local.json" set "JSONS=!JSONS! %%~nxF"
)
set "ARCHIVE_NAME=EmpireCraftCore-%RANDOM%-%RANDOM%.tar.gz"
set "ARCHIVE=%TEMP%\%ARCHIVE_NAME%"
echo Compressing, this takes a little while...
tar.exe -czf "%ARCHIVE%" --exclude=*.cs --exclude=*.meta --exclude=*.pdb -C "%ROOT%.server\publish" linux-x64 -C "%ROOT%Tools\StandaloneServer" install.sh -C "%ROOT%." %DATA_DIRS% %JSONS%
if errorlevel 1 (
    if exist "%ARCHIVE%" del /q "%ARCHIVE%"
    echo Failed to package. Nothing was sent.
    set "RESULT=1"
    goto :finish
)
for %%A in ("%ARCHIVE%") do echo Package size: %%~zA bytes

echo.
echo ===== Upload and restart on %SERVER% =====
echo If SSH asks for the %SERVER_USER% password, typing it will not show any characters.
where scp.exe >nul 2>&1
if errorlevel 1 (
    del /q "%ARCHIVE%"
    echo scp.exe was not found. Install the Windows OpenSSH Client and try again.
    set "RESULT=1"
    goto :finish
)
ssh.exe %SERVER_USER%@%SERVER% "mkdir -p %SERVER_DIR%"
rem scp shows progress (percent, speed, time left). Run it from the temp folder so the local path has no drive colon.
pushd "%TEMP%"
scp.exe "%ARCHIVE_NAME%" %SERVER_USER%@%SERVER%:%SERVER_DIR%/.incoming.tar.gz
set "SCP_RESULT=!errorlevel!"
popd
if not "!SCP_RESULT!"=="0" (
    del /q "%ARCHIVE%"
    echo Upload failed. Nothing was changed on the server.
    set "RESULT=1"
    goto :finish
)
echo Upload finished. Installing and restarting on the server...
ssh.exe %SERVER_USER%@%SERVER% "rm -rf %SERVER_DIR%/.incoming && mkdir -p %SERVER_DIR%/.incoming && tar -xzf %SERVER_DIR%/.incoming.tar.gz --no-same-owner -C %SERVER_DIR%/.incoming && rm -f %SERVER_DIR%/.incoming.tar.gz && sed -i 's/\r$//' %SERVER_DIR%/.incoming/install.sh && bash %SERVER_DIR%/.incoming/install.sh %SERVER_DIR%"
set "SSH_RESULT=%errorlevel%"
del /q "%ARCHIVE%"
if not "%SSH_RESULT%"=="0" (
    echo Server update failed. See the SSH or installation messages above.
    set "RESULT=1"
    goto :finish
)
echo Server update completed.

:check
if not defined CORE_PORT (
    if /i "%MODE%"=="check" echo Set CORE_PORT at the top of this file to the core's port first.
    goto :finish
)
echo.
echo ===== Check from this PC =====
where curl.exe >nul 2>&1
if errorlevel 1 (
    echo curl.exe is required for the check.
    goto :finish
)
curl.exe -fsS --max-time 20 "http://%SERVER%:%CORE_PORT%/api/server" | findstr.exe /c:"empirecraft" >nul
if errorlevel 1 (
    echo Core did not answer at http://%SERVER%:%CORE_PORT%/api/server  ^(port not open in the cloud security group, or the core is down^)
    set "RESULT=1"
) else (
    echo Core is answering at http://%SERVER%:%CORE_PORT%/
)

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
