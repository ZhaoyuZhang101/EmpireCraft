# 一键发布：推送代码 → 编译检查 → 更新独立服务器(核心与手机网页) → 发布模组更新(玩家游戏里的在线更新)。
#   .\publish_all.bat                 全做
#   .\publish_all.bat -NoPush         不推送 GitHub
#   .\publish_all.bat -NoServer       不更新服务器
#   .\publish_all.bat -NoClient       不发布模组更新
#   .\publish_all.bat -Lobby          另外更新联机大厅(update_lobby.bat)
# 只发布已经提交的东西：在一个干净的临时副本(当前提交)里编译、打包，工作区里没提交的改动(包括别的会话改到一半的)一概不带。
# 模组更新那一步要输入 Cloudflare 令牌、服务器那一步可能要输 SSH 密码：都在提示处自己输，脚本不存。
[CmdletBinding()]
param(
    [switch]$NoPush,
    [switch]$NoServer,
    [switch]$NoClient,
    [switch]$Lobby,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$Root = $PSScriptRoot
# 临时副本放在游戏目录下两层(同模组目录一样深)：模组工程按 ..\..\worldbox_Data 找游戏的库；不放进 Mods，免得被当成模组加载
$Game = (Resolve-Path (Join-Path $Root "..\..")).Path
$WorkParent = Join-Path $Game "ec_release"
$Work = Join-Path $WorkParent "EmpireCraft"
$failed = @()

function Step([string]$title) { Write-Host ""; Write-Host "===== $title =====" -ForegroundColor Cyan }

# git 的进度提示走 stderr：PowerShell 5.1 在"出错即停"下会把它当成错误，这里放宽了再看退出码
function Invoke-RepoGit {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { $output = @(& git -C $Root @args 2>&1 | ForEach-Object { "$_" }) }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') 失败：`n$($output -join "`n")" }
    return $output
}

function Remove-Work {
    if (Test-Path $Work) {
        try { Invoke-RepoGit worktree remove --force $Work | Out-Null } catch { }
        if (Test-Path $Work) { Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue }
    }
    try { Invoke-RepoGit worktree prune | Out-Null } catch { }
}

# ── 看一眼要发布的是什么 ──
$branch = (Invoke-RepoGit rev-parse --abbrev-ref HEAD) | Select-Object -First 1
$head = (Invoke-RepoGit log -1 --format="%h %s") | Select-Object -First 1
$version = (Get-Content -Raw -Encoding UTF8 (Join-Path $Root "mod.json") | ConvertFrom-Json).version
$dirty = @(Invoke-RepoGit status --porcelain --untracked-files=no)
$ahead = 0
try { $ahead = @(Invoke-RepoGit log --oneline "origin/$branch..HEAD").Count } catch { $ahead = -1 }
Step "要发布的"
Write-Host "  分支 $branch，提交 $head"
Write-Host "  模组版本 $version (mod.json)"
Write-Host "  还没推送的提交 $ahead 个"
if ($dirty.Count -gt 0) {
    Write-Host "  工作区有 $($dirty.Count) 个文件改了没提交：这些不会发布(要发布先提交)" -ForegroundColor Yellow
}
Write-Host ("  要做：" + (@(
    $(if (-not $NoPush) { "推送" }), "编译检查", $(if (-not $NoServer) { "更新服务器" }), $(if (-not $NoClient) { "发布模组更新" }),
    $(if ($Lobby) { "更新大厅" })) | Where-Object { $_ }) -join " → ")
if (-not $Yes) {
    $answer = Read-Host "继续吗？(y/n)"
    if ($answer -notin @("y", "Y", "yes")) { Write-Host "没做任何事。"; exit 0 }
}

try {
    # ── 1. 推送 ──
    if (-not $NoPush) {
        Step "推送 GitHub"
        try { Invoke-RepoGit push origin $branch | ForEach-Object { Write-Host "  $_" } }
        catch { throw "推送失败(网络或远端比本地新)。什么都没发布。`n$($_.Exception.Message)" }
    }

    # ── 2. 干净副本 + 编译检查 ──
    Step "准备干净副本(当前提交)"
    Remove-Work
    New-Item -ItemType Directory -Force $WorkParent | Out-Null
    Invoke-RepoGit worktree add --detach $Work HEAD | Out-Null
    foreach ($ignored in @("release.ps1", "release.cmd")) {
        if (Test-Path (Join-Path $Root $ignored)) { Copy-Item (Join-Path $Root $ignored) (Join-Path $Work $ignored) -Force }
    }
    Write-Host "  $Work"

    Step "编译检查(模组)"
    & dotnet build (Join-Path $Work "EmpireCraft.csproj") -c Debug -v q -nologo -clp:"NoSummary;ErrorsOnly"
    if ($LASTEXITCODE -ne 0) { throw "模组编译不过：游戏里会整个加载不了。先修好再发布。" }
    Write-Host "  通过"

    # ── 3. 服务器 ──
    if (-not $NoServer) {
        Step "更新独立服务器(编译核心、上传、重启)"
        $env:EC_NOPAUSE = "1"
        & cmd.exe /c (Join-Path $Work "update_server.bat")
        if ($LASTEXITCODE -ne 0) { $failed += "服务器" }
    }

    # ── 4. 模组更新 ──
    if (-not $NoClient) {
        Step "发布模组更新 $version"
        if (-not (Test-Path (Join-Path $Work "release.ps1"))) { throw "找不到 release.ps1(它不在仓库里，要在 $Root 下)。" }
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Work "release.ps1")
        $released = $LASTEXITCODE -eq 0
        # 打好的包拷回项目的 dist\
        $zips = @(Get-ChildItem (Join-Path $Work "dist") -Filter "*.zip" -ErrorAction SilentlyContinue)
        if ($zips.Count -gt 0) {
            New-Item -ItemType Directory -Force (Join-Path $Root "dist") | Out-Null
            foreach ($zip in $zips) { Copy-Item $zip.FullName (Join-Path $Root "dist") -Force; Write-Host "  包在 $(Join-Path $Root "dist\$($zip.Name)")" }
        }
        if (-not $released) { $failed += "模组更新" }
        else {
            try {
                $latest = Invoke-RestMethod -Uri "https://update.cmhgamedev.dpdns.org/releases/latest.json" -TimeoutSec 20
                Write-Host "  线上最新版本：$($latest.version)" $(if ($latest.version -eq $version) { "(对上了)" } else { "(还不是 $version，等一会儿再看)" })
            } catch { Write-Host "  线上版本查不到：$($_.Exception.Message)" -ForegroundColor Yellow }
        }
    }

    # ── 5. 大厅 ──
    if ($Lobby) {
        Step "更新联机大厅"
        $env:EC_NOPAUSE = "1"
        & cmd.exe /c (Join-Path $Root "update_lobby.bat")
        if ($LASTEXITCODE -ne 0) { $failed += "大厅" }
    }
}
catch {
    Write-Host ""
    Write-Host $_.Exception.Message -ForegroundColor Red
    $failed += "中途停下"
}
finally {
    Remove-Work
}

Write-Host ""
if ($failed.Count -eq 0) { Write-Host "全部完成。" -ForegroundColor Green; exit 0 }
Write-Host ("没成的：" + ($failed -join "、") + "。看上面的说明。") -ForegroundColor Red
exit 1
