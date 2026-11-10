@echo off
rem 只编译、不打包、不发布(发布走 release.cmd)。输出写进 build.log，方便直接读
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet CLI not found - build from Rider or Visual Studio instead.> build.log
    type build.log
    pause
    exit /b 1
)

echo Building EmpireCraft.csproj ...
dotnet build EmpireCraft.csproj -v n > build.log 2>&1
set RC=%errorlevel%
echo.>> build.log
echo ExitCode=%RC%>> build.log

echo.
if %RC% neq 0 (
    echo BUILD FAILED. First errors:
    findstr /C:": error" build.log
) else (
    echo BUILD OK.
)
echo.
echo Full output: build.log
pause
