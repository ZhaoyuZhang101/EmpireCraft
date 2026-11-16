# Update the Windows development install used by the editor and desktop shortcut.
param([switch]$IfInstalled)

$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$parent = Join-Path $game 'EmpireCraftCore'
$install = Join-Path $parent 'win-x64'
$exe = Join-Path $install 'EmpireCraftServer.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    if ($IfInstalled) { Write-Host 'No local Windows development core installed. Skipping.'; exit 0 }
    throw "Local Windows core not found at $exe"
}

$id = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $parent "win-x64.stage-$id"
$backup = Join-Path $parent 'win-x64.old'
$lock = $null

function Assert-LocalDirectory([string]$path) {
    $directory = Get-Item -LiteralPath $path -Force
    if (-not $directory.PSIsContainer -or $directory.FullName -ne $path -or
        ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unexpected Windows core directory: $path"
    }
}

function Get-Option([string]$arguments, [string]$name) {
    if ($arguments -match ('(?:^|\s)--' + $name + '\s+(?:"([^"]+)"|(\S+))')) {
        if ($Matches[1]) { return $Matches[1] }
        return $Matches[2]
    }
}

function Stop-StandaloneCore($process, [string]$arguments) {
    $state = Get-Option $arguments 'state'
    if (-not $state -or -not [IO.Path]::IsPathRooted($state) -or $arguments -match '(?:^|\s)--new(?:\s|$)') {
        throw 'Stop this Windows core from its panel and rerun the update. Its launch options cannot be safely reused.'
    }
    $configPath = Get-Option $arguments 'config'
    if (-not $configPath) { $configPath = Join-Path $state 'server.json' }
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $port = Get-Option $arguments 'port'
    if (-not $port) { $port = $config.port }
    if (-not $port) { $port = 8766 }
    if (-not (Get-NetTCPConnection -State Listen -LocalPort $port -OwningProcess $process.Id -ErrorAction SilentlyContinue)) {
        throw 'The Windows core is starting or its configured port is not listening. Wait for it to finish or close it from its panel.'
    }
    $base = "http://127.0.0.1:$port/api/admin"
    if ($config.admin_password) {
        $login = @{ user = $config.admin_user; password = $config.admin_password }
    } else {
        $passPath = Join-Path $state 'mods_config\EmpireCraft.terminal.admin'
        $login = @{ code = (Get-Content -LiteralPath $passPath -TotalCount 1).Trim() }
    }
    $http = @{ UseBasicParsing = $true; TimeoutSec = 10; MaximumRedirection = 0 }
    $session = Invoke-RestMethod @http -Uri "$base/pair" -Method Post -ContentType 'application/json' -Body ($login | ConvertTo-Json -Compress)
    $http.Headers = @{ 'X-Terminal-Token' = $session.token }
    $current = Invoke-RestMethod @http -Uri "$base/save"
    if ($current.detached) {
        throw 'The Windows core was handed off by the game. Return to the game or close the core from its tray, then rerun the update.'
    }
    $scenario = Invoke-RestMethod @http -Uri "$base/scenario"
    if ($scenario.state -eq 'loading') { throw 'The Windows core is loading a scenario. Try again when the world has loaded.' }
    Write-Host 'Stopping the Windows core so it can save...'
    # Keep the process handle open so Windows PowerShell can read its exit code.
    $null = $process.Handle
    Invoke-RestMethod @http -Uri "$base/return" -Method Post | Out-Null
    if (-not $process.WaitForExit(120000)) { throw 'The Windows core is still saving. Rerun the update after it exits.' }
    if ($process.ExitCode -ne 0) { throw "The Windows core exited with code $($process.ExitCode)." }
}

try {
    Assert-LocalDirectory $parent
    Assert-LocalDirectory $install
    $lock = [IO.File]::Open((Join-Path $parent 'update.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    & dotnet publish (Join-Path $PSScriptRoot 'EmpireCraft.Server.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=none -o $stage -v q -nologo
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $stage 'EmpireCraftServer.exe') -PathType Leaf)) {
        throw 'Windows core build failed.'
    }
    Assert-LocalDirectory $stage
    if (Test-Path -LiteralPath $backup) {
        Assert-LocalDirectory $backup
        Remove-Item -LiteralPath $backup -Recurse -Force
    }

    $core = Get-CimInstance Win32_Process -Filter "Name = 'EmpireCraftServer.exe'" | Where-Object ExecutablePath -eq $exe | Select-Object -First 1
    if ($core) {
        $process = Get-Process -Id $core.ProcessId
        $arguments = $core.CommandLine -replace '^\s*(?:"[^"]+"|\S+)\s*', ''
        Stop-StandaloneCore $process $arguments
    }
    try {
        Move-Item -LiteralPath $install -Destination $backup
        try { Move-Item -LiteralPath $stage -Destination $install }
        catch {
            Move-Item -LiteralPath $backup -Destination $install
            throw
        }
    } finally {
        if ($core -and (Test-Path -LiteralPath $exe -PathType Leaf)) {
            $started = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $install -WindowStyle Hidden -PassThru
            if ($started.WaitForExit(2000)) { throw "The Windows core failed to restart (exit $($started.ExitCode))." }
        }
    }
    Write-Host "Updated local Windows core at $install"
} finally {
    if ($lock) { $lock.Dispose() }
    if (Test-Path -LiteralPath $stage) {
        Assert-LocalDirectory $stage
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
