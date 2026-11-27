# 发布编辑器更新(发布编辑器更新.bat 调它)：打安装程序 → 传到更新服务 → 发清单。装好的编辑器照清单自己更新(见 updater.js)。
#   .\发布编辑器更新.bat                 全做
#   .\发布编辑器更新.bat -NoBuild        不重新打包，传 dist 里现成的那个安装程序
#   .\发布编辑器更新.bat -Notes "……"     这一版的说明(更新窗口里显示)
# 用的是模组更新那个服务和同一个令牌(在提示处自己输，脚本不存；也可以先设环境变量 EC_RELEASE_TOKEN)。
# 安装程序一百来兆，更新服务一回收不了这么大的就切成几片传(-PartMB，默认 90)，编辑器下载时按清单接起来。
[CmdletBinding()]
param(
    [string]$UpdateUrl = "https://update.cmhai.tech",
    [string]$Notes = "",
    [int]$PartMB = 90,
    [switch]$NoBuild,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)
# Windows PowerShell 5.1 otherwise adds a BOM to curl's stdin after chcp 65001.
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
$here = $PSScriptRoot
$base = $UpdateUrl.TrimEnd("/")
# 更新服务只收"EmpireCraft_Ver_某某.zip"这种名字(模组包的名字；别的名字回 400 Invalid release name，2026-11-16 头一回发布撞上)。
# 它到底怎么查的手头没有源码，所以备了三种写法，探路时挨个试，用它肯收的头一个。
# 清单的名字和 updater.js 的 MANIFESTS 对着：这里添了、改了，那边也要跟着改。{0} 是版本号，{1} 是分片的后缀
$schemes = @(
    @{ manifest = "EmpireCraft_Ver_0.0.0-EditorManifest.zip"; probe = "EmpireCraft_Ver_0.0.0-EditorProbe.zip"; file = "EmpireCraft_Ver_{0}-Editor{1}.zip" },
    @{ manifest = "EmpireCraft_Ver_0.0.0EditorManifest.zip"; probe = "EmpireCraft_Ver_0.0.0EditorProbe.zip"; file = "EmpireCraft_Ver_{0}Editor{1}.zip" },
    @{ manifest = "EmpireCraft_Ver_EditorManifest.zip"; probe = "EmpireCraft_Ver_EditorProbe.zip"; file = "EmpireCraft_Ver_Editor{0}{1}.zip" }
)

function Step([string]$title) { Write-Host ""; Write-Host "===== $title =====" -ForegroundColor Cyan }

# 线上的清单(名字是 .zip，内容是 JSON，所以自己解)。读不到返回空
function Read-OnlineManifest([string]$Name) {
    try {
        $response = Invoke-WebRequest -Uri "$base/releases/$Name" -TimeoutSec 20 -UseBasicParsing
        $text = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        return $text.TrimStart([char]0xFEFF) | ConvertFrom-Json
    }
    catch { return $null }
}

$stage = Join-Path ([IO.Path]::GetTempPath()) ("ec-editor-upload-" + [guid]::NewGuid().ToString("N"))
try {
    if (-not $NoBuild) {
        Step "打安装程序"
        & node (Join-Path $here "pack-editor.js") nsis
        if ($LASTEXITCODE -ne 0) { throw "安装程序没打成，什么都没发。" }
    }

    $version = (Get-Content -Raw -Encoding UTF8 (Join-Path $here "package.json") | ConvertFrom-Json).version
    $installer = Join-Path $here "dist\EmpireCraft Editor Setup $version.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw "没找到安装程序：$installer(先不带 -NoBuild 跑一遍)。" }
    # 下到玩家电脑上存成的名字(线上的名字另起，见 $schemes)
    $saveAs = "EmpireCraftEditor_Setup_$version.exe"
    # 没给 -Notes 就用 EDITOR_NOTES.md 里写的(每发一版改一改)
    $notesFile = Join-Path $here "EDITOR_NOTES.md"
    if ([string]::IsNullOrWhiteSpace($Notes) -and (Test-Path -LiteralPath $notesFile)) {
        $Notes = ([string](Get-Content -LiteralPath $notesFile -Raw -Encoding UTF8)).Trim()
    }
    $size = (Get-Item -LiteralPath $installer).Length
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()

    Step "要发布的"
    Write-Host "  编辑器 $version，安装程序 $([math]::Round($size / 1MB, 1)) MB"
    $online = $null
    foreach ($scheme in $schemes) {
        $found = Read-OnlineManifest $scheme.manifest
        if ($found -and $found.version) { $online = $found.version; break }
    }
    Write-Host ("  线上现在：" + $(if ($online) { $online } else { "还没发过" }))
    if ($online -eq $version) {
        Write-Host "  线上已经是 $version：版本号没变，装着这一版的编辑器不会再更新。要让它们更新，先把 package.json 里的 version 调高。" -ForegroundColor Yellow
    }
    if (-not $Yes) {
        $answer = Read-Host "继续吗？(y/n)"
        if ($answer -notin @("y", "Y", "yes")) { Write-Host "没做任何事。"; exit 0 }
    }

    $token = $env:EC_RELEASE_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) {
        $secure = Read-Host "EmpireCraft Cloudflare release token" -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try { $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    }
    if ([string]::IsNullOrWhiteSpace($token)) { throw "没有令牌，什么都没发。" }

    # 传一个文件。令牌经标准输入交给 curl(不出现在命令行里)；进度条 curl 自己画
    function Send-File([string]$Path, [string]$RemoteName, [string]$ContentType, [switch]$Quiet) {
        $reply = Join-Path $stage "reply.txt"
        $config = "header = `"Authorization: Bearer $token`"`nheader = `"Content-Type: $ContentType`"`nheader = `"Expect:`"`n"
        $arguments = @("--config", "-", "--show-error", "--location", "--connect-timeout", "15", "--max-time", "3600", "--http1.1",
            "--retry", "2", "--retry-delay", "3", "--request", "PUT", "--data-binary", "@$Path", "--output", $reply, "--write-out", "%{http_code}")
        $arguments += $(if ($Quiet) { "--silent" } else { "--progress-bar" })
        $arguments += "$base/admin/releases/$([Uri]::EscapeDataString($RemoteName))"
        $code = $config | & curl.exe @arguments
        $body = if (Test-Path -LiteralPath $reply) { [string](Get-Content -LiteralPath $reply -Raw -Encoding UTF8) } else { "" }
        if ($LASTEXITCODE -ne 0) { throw "传 $RemoteName 没成(curl 退出码 $LASTEXITCODE)。" }
        if ("$code" -notmatch '^2\d\d$') {
            if ($body.Length -gt 300) { $body = $body.Substring(0, 300) }
            throw "更新服务不收 $RemoteName(HTTP $code)：$body"
        }
    }

    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    Step "试一下更新服务"
    # 先传一个几十个字节的小文件：令牌不对、服务不收这类名字的文件，在这里就知道，不用等一百多兆传完
    $probe = Join-Path $stage "probe.json"
    [IO.File]::WriteAllText($probe, "{`"probe`":`"$version`"}", $utf8)
    $chosen = $null
    $refused = @()
    foreach ($scheme in $schemes) {
        try {
            Send-File $probe $scheme.probe "application/zip" -Quiet
            $chosen = $scheme
            break
        }
        catch {
            $refused += "  " + $_.Exception.Message
            # 令牌不对、连不上：换名字也没用
            if ($_.Exception.Message -notmatch 'HTTP 400') { break }
        }
    }
    if (-not $chosen) {
        if ($refused.Count -eq 1) { throw ($refused[0].Trim() + " 什么都没发。") }
        throw ("更新服务一种名字都不收，什么都没发：`n" + ($refused -join "`n"))
    }
    $manifestName = $chosen.manifest
    $remoteName = $chosen.file -f $version, ""
    Write-Host "  通了，线上用的名字：$remoteName"

    Step "传安装程序"
    $limit = [int64]$PartMB * 1MB
    $parts = @()
    if ($size -le $limit) {
        $copy = Join-Path $stage $remoteName
        Copy-Item -LiteralPath $installer -Destination $copy
        Send-File $copy $remoteName "application/zip"
    }
    else {
        # 切片：一片一个文件，传完一片删一片
        $count = [int][math]::Ceiling($size / $limit)
        Write-Host "  $([math]::Round($size / 1MB, 1)) MB，切成 $count 片传"
        $source = [IO.File]::OpenRead($installer)
        try {
            $buffer = New-Object byte[] (4MB)
            for ($index = 1; $index -le $count; $index++) {
                $partName = $chosen.file -f $version, ".part$index"
                $partPath = Join-Path $stage $partName
                $left = [math]::Min($limit, $size - $source.Position)
                $partSize = $left
                $target = [IO.File]::Create($partPath)
                try {
                    while ($left -gt 0) {
                        $read = $source.Read($buffer, 0, [int][math]::Min($buffer.Length, $left))
                        if ($read -le 0) { break }
                        $target.Write($buffer, 0, $read)
                        $left -= $read
                    }
                }
                finally { $target.Dispose() }
                Write-Host "  第 $index / $count 片"
                Send-File $partPath $partName "application/zip"
                Remove-Item -LiteralPath $partPath -Force
                $parts += [ordered]@{ fileName = $partName; size = $partSize }
            }
        }
        finally { $source.Dispose() }
    }

    Step "发清单"
    # 清单最后发：编辑器看到新版本号时，安装程序已经都在线上了
    $manifest = [ordered]@{ version = $version; fileName = $remoteName; saveAs = $saveAs; sha256 = $hash; size = $size; notes = $Notes }
    if ($parts.Count -gt 0) { $manifest.parts = $parts }
    $manifestPath = Join-Path $stage $manifestName
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $utf8)
    Send-File $manifestPath $manifestName "application/zip" -Quiet

    $seen = (Read-OnlineManifest $manifestName).version
    Write-Host ""
    if ($seen -eq $version) { Write-Host "编辑器 $version 发好了：装好的编辑器打开时会在顶栏提示新版。" -ForegroundColor Green }
    else { Write-Host "传完了，但线上清单读出来是「$seen」，不是 $version：过一会儿再看 $base/releases/$manifestName" -ForegroundColor Yellow }
    exit 0
}
catch {
    Write-Host ""
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
