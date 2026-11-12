<#
  帝国盒子 发布打包脚本（在你本机 Windows 上跑，需已装 WorldBox + .NET SDK）。
  用户 2026-11-08：每次发布版本时，把模组编译成 DLL（可选保守混淆），发布包只放编译产物 + 数据资源，不放 .cs 源码。

  做的事：
    1. dotnet build 出 Release 版 EmpireCraft.dll（本机有游戏程序集才能编译）；
    2. 可选：用 Obfuscar 保守混淆（配置见 build/Obfuscar.xml）；
    3. 组装发布目录：数据资源（Locales/Technology/GameResources/... + 各 json + mod.json + icon）+ 这一个 DLL，
       明确排除 Scripts/ 等源码与开发文件；
    4. 压成 EmpireCraft-<版本>.zip。

  用法（在仓库根目录）：
    pwsh build/pack-release.ps1                 # 编译 + 混淆 + 打包（默认）
    pwsh build/pack-release.ps1 -NoObfuscate    # 只编译成 DLL，不混淆
    pwsh build/pack-release.ps1 -OutputDir D:\somewhere

  前置：
    · .NET SDK（dotnet）；
    · 混淆需要 Obfuscar：  dotnet tool install --global Obfuscar.GlobalTool
    · 发布包做好后，务必在游戏里实测：开新世界 + 读一个旧存档（混淆最可能坏的就是旧档）。
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$NoObfuscate,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $OutputDir) { $OutputDir = Join-Path $Root "dist" }

function Info($m) { Write-Host "[打包] $m" -ForegroundColor Cyan }
function Warn($m) { Write-Host "[打包] $m" -ForegroundColor Yellow }

# —— 版本号（取 mod.json） ——
$modJson = Get-Content (Join-Path $Root "mod.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$version = $modJson.version
if (-not $version) { $version = "0.0.0" }
Info "版本 $version"

# —— 1. 编译 Release DLL ——
# 注意：csproj 在主 PropertyGroup 里硬写了 DefineConstants=DEBUG;TRACE，这里覆盖成 TRACE，去掉 DEBUG，出干净的发布版。
Info "编译 $Configuration ..."
& dotnet build (Join-Path $Root "EmpireCraft.csproj") -c $Configuration -p:DefineConstants="TRACE" --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "编译失败（确认本机能访问 ..\..\worldbox_Data\Managed 下的游戏程序集）" }

$builtDll = Join-Path $Root "bin\$Configuration\net48\EmpireCraft.dll"
if (-not (Test-Path $builtDll)) { throw "没找到编译产物：$builtDll" }

# —— 2. 可选：保守混淆 ——
$releaseDll = $builtDll
if (-not $NoObfuscate) {
    $obfuscar = Get-Command obfuscar -ErrorAction SilentlyContinue
    if (-not $obfuscar) {
        throw "没找到 obfuscar。先装： dotnet tool install --global Obfuscar.GlobalTool （或用 -NoObfuscate 跳过混淆）"
    }
    $inDir  = Join-Path $Root "build\_in"
    $outDir = Join-Path $Root "build\_out"
    Remove-Item $inDir, $outDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $inDir, $outDir | Out-Null
    Copy-Item $builtDll (Join-Path $inDir "EmpireCraft.dll") -Force

    Info "混淆（保守档，见 build/Obfuscar.xml）..."
    Push-Location $Root
    try { & obfuscar "build\Obfuscar.xml" } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "混淆失败" }

    $releaseDll = Join-Path $outDir "EmpireCraft.dll"
    if (-not (Test-Path $releaseDll)) { throw "没找到混淆产物：$releaseDll" }
}
else { Warn "按要求跳过混淆，只用编译产物" }

# —— 3. 组装发布目录（白名单：只带这些，其它一概不进包；源码 Scripts/ 不发布） ——
$stage = Join-Path $OutputDir "EmpireCraft"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# 要带的数据资源目录（模组运行时要读）
$includeDirs = @("Locales","Technology","GameResources","InstitutionTrees","WorldMaps","Music","Assets","RegimeEditor","Terminal")
# 要带的顶层文件
$includeFiles = @("mod.json","icon.png","default_config.json",
                  "ClaimRules.json","ConstitutionTemplates.json","IdeologyBureaus.json","ReligionTenets.json",
                  "LICENSE","README.md")

foreach ($d in $includeDirs) {
    $src = Join-Path $Root $d
    if (-not (Test-Path $src)) {
        Warn "缺少数据目录（跳过）：$d"
        continue
    }
    if ($d -eq "RegimeEditor") {
        # 桌面编辑器的 npm 依赖和本机打包产物不随游戏模组分发。
        $editorStage = Join-Path $stage $d
        New-Item -ItemType Directory -Path $editorStage | Out-Null
        Get-ChildItem -LiteralPath $src -Force | Where-Object { $_.Name -notin @("node_modules", "dist") } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $editorStage -Recurse -Force }
    }
    else { Copy-Item $src $stage -Recurse -Force }
}
foreach ($f in $includeFiles) {
    $src = Join-Path $Root $f
    if (Test-Path $src) { Copy-Item $src $stage -Force }
    else { Warn "缺少文件（跳过）：$f" }
}

# 放入唯一的一个 DLL（NML 会 Assembly.Load 模组根目录下的每个 .dll，所以绝不能把 Harmony/Newtonsoft 等依赖也拷进来）
Copy-Item $releaseDll (Join-Path $stage "EmpireCraft.dll") -Force

# 安全校验：根目录只能有 EmpireCraft.dll 一个 dll，且绝不能混进 .cs
$dlls = Get-ChildItem $stage -Filter *.dll
if ($dlls.Count -ne 1) { throw "发布根目录出现了 $($dlls.Count) 个 dll（只应有 EmpireCraft.dll）：$($dlls.Name -join ', ')" }
$leakedCs = Get-ChildItem $stage -Recurse -Filter *.cs -ErrorAction SilentlyContinue
if ($leakedCs) { throw "发布包里混进了源码 .cs（$($leakedCs.Count) 个），已中止。请检查白名单。" }
if (-not (Test-Path -LiteralPath (Join-Path $stage "Terminal/index.html") -PathType Leaf)) {
    throw "发布包缺少终端网页 Terminal/index.html。"
}

# —— 4. 压包 ——
$zip = Join-Path $OutputDir ("EmpireCraft-" + $version + ".zip")
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force

Info "完成："
Info "  发布目录：$stage"
Info "  压缩包：  $zip"
Info "下一步：把发布目录作为模组文件夹装进游戏实测（开新世界 + 读一个旧存档），没问题再上传创意工坊。"
