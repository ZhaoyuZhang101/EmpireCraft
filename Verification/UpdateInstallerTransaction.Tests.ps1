$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $PSScriptRoot ('update-installer-' + [Guid]::NewGuid().ToString('N'))
$workspace = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$fixtureFull = [IO.Path]::GetFullPath($fixture)
if (!$fixtureFull.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The test fixture must stay inside the workspace.'
}

$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'bin/Debug/net48/EmpireCraft.dll'))
$service = $assembly.GetType('EmpireCraft.Scripts.Diagnostics.EmpireCraftUpdateService', $true)
$buildScript = $service.GetMethod('BuildInstallScript', [Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $buildScript) { throw 'BuildInstallScript was not found.' }

function Invoke-Scenario([string]$name, [bool]$failAfterSwap,
                         [bool]$gitWorktree = $false, [bool]$packageGitMarker = $false,
                         [bool]$existingBackup = $false, [bool]$archiveVersion = $false,
                         [bool]$archiveFailure = $false) {
    $directory = Join-Path $fixture $name
    $source = Join-Path $directory 'package'
    $target = Join-Path $directory 'EmpireCraft'
    $candidate = Join-Path $fixture ('.EmpireCraft.candidate-' + $name)
    $backup = Join-Path $fixture ('.EmpireCraft.backup-' + $name)
    $state = Join-Path $directory 'state'
    $archiveFolder = Join-Path $directory 'ArchivedVersions'
    $archive = Join-Path $archiveFolder 'EmpireCraft_1.0.ecbackup'
    foreach ($path in @($source, (Join-Path $source 'Scripts'), $target,
                       (Join-Path $target 'Scripts'), $state)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $source 'mod.json'), '{"version":"2.0"}')
    [IO.File]::WriteAllText((Join-Path $source 'default_config.json'), '{"setting":"new"}')
    [IO.File]::WriteAllText((Join-Path $source 'Scripts/new.cs'), 'new')
    [IO.File]::WriteAllText((Join-Path $target 'mod.json'), '{"version":"1.0"}')
    [IO.File]::WriteAllText((Join-Path $target 'default_config.json'), '{"setting":"custom"}')
    [IO.File]::WriteAllText((Join-Path $target 'custom.txt'), 'keep')
    [IO.File]::WriteAllText((Join-Path $target 'Scripts/old.cs'), 'old')
    if ($gitWorktree) {
        [IO.File]::WriteAllText((Join-Path $target '.git'), 'gitdir: ../worktrees/example')
    }
    if ($packageGitMarker) {
        [IO.File]::WriteAllText((Join-Path $source '.git'), 'gitdir: ../worktrees/example')
    }
    if ($existingBackup) {
        New-Item -ItemType Directory -Path $backup | Out-Null
        [IO.File]::WriteAllText((Join-Path $backup 'mod.json'), '{"version":"0.5"}')
    }
    if ($archiveFailure) {
        [IO.File]::WriteAllText($archiveFolder, 'blocks archive directory creation')
    }

    $result = Join-Path $state 'result.json'
    $pending = Join-Path $state 'pending.json'
    $log = if ($failAfterSwap) { Join-Path $target 'Scripts' } else { Join-Path $state 'install.log' }
    [object[]]$arguments = @(
        [int]99999999, [string]'EmpireCraftUpdaterTestNeverRunning', [string]$source,
        [string]$target, [string]'2.0', [string](Join-Path $state 'package.zip'),
        [string]$log, [string]$pending, [string]$result, [string]$candidate, [string]$backup,
        [string]$(if ($archiveVersion -or $archiveFailure) { $archive } else { $null })
    )
    $script = $buildScript.Invoke($null, $arguments)
    $scriptPath = Join-Path $state 'install.ps1'
    [IO.File]::WriteAllText($scriptPath, $script, (New-Object Text.UTF8Encoding($false)))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath | Out-Null
    if (!(Test-Path -LiteralPath $result)) { throw "No installer result for $name." }
    $outcome = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if ($gitWorktree -or $packageGitMarker) {
        if ($outcome.status -ne 'failure' -or
            (Get-Content -LiteralPath (Join-Path $target 'mod.json') -Raw | ConvertFrom-Json).version -ne '1.0' -or
            (Test-Path -LiteralPath $backup)) {
            throw 'A Git checkout marker was not protected from automatic replacement.'
        }
    }
    elseif ($existingBackup) {
        if ($outcome.status -ne 'failure' -or
            (Get-Content -LiteralPath (Join-Path $target 'mod.json') -Raw | ConvertFrom-Json).version -ne '1.0' -or
            (Get-Content -LiteralPath (Join-Path $backup 'mod.json') -Raw | ConvertFrom-Json).version -ne '0.5') {
            throw 'An existing backup changed the installed mod.'
        }
    }
    elseif ($failAfterSwap -or $archiveFailure) {
        if ($outcome.status -ne 'failure' -or
            (Get-Content -LiteralPath (Join-Path $target 'mod.json') -Raw | ConvertFrom-Json).version -ne '1.0' -or
            !(Test-Path -LiteralPath (Join-Path $target 'Scripts/old.cs')) -or
            ($archiveFailure -and (Test-Path -LiteralPath $archive))) {
            throw 'The previous installation was not restored after the simulated failure.'
        }
    }
    else {
        if ($outcome.status -ne 'success' -or
            (Get-Content -LiteralPath (Join-Path $target 'mod.json') -Raw | ConvertFrom-Json).version -ne '2.0' -or
            !(Test-Path -LiteralPath (Join-Path $target 'Scripts/new.cs')) -or
            (Test-Path -LiteralPath (Join-Path $target 'Scripts/old.cs')) -or
            !(Test-Path -LiteralPath (Join-Path $target 'custom.txt')) -or
            (Get-Content -LiteralPath (Join-Path $target 'default_config.json') -Raw | ConvertFrom-Json).setting -ne 'custom' -or
            (Test-Path -LiteralPath $backup) -or
            (!$archiveVersion -and (Test-Path -LiteralPath $archive))) {
            throw 'The successful install did not preserve the expected data.'
        }
        if ($archiveVersion) {
            if (!(Test-Path -LiteralPath $archive) -or
                [IO.Path]::GetExtension($archive) -ne '.ecbackup') {
                throw 'The version archive was not created with the custom extension.'
            }
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [IO.Compression.ZipFile]::OpenRead($archive)
            try {
                $entryNames = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
                if ($null -eq $zip.GetEntry('mod.json') -or
                    $entryNames -notcontains 'Scripts/old.cs' -or
                    $entryNames -notcontains 'default_config.json') {
                    throw ('The version archive is incomplete: ' + ($entryNames -join ', '))
                }
                $reader = New-Object IO.StreamReader($zip.GetEntry('mod.json').Open())
                try {
                    $archivedManifest = $reader.ReadToEnd() | ConvertFrom-Json
                    if ($archivedManifest.version -ne '1.0') {
                        throw 'The archive contains the new version instead of the replaced version.'
                    }
                }
                finally { $reader.Dispose() }
            }
            finally { $zip.Dispose() }
        }
    }
}

try {
    Invoke-Scenario 'success' $false
    Invoke-Scenario 'rollback' $true
    Invoke-Scenario 'git-worktree' $false $true
    Invoke-Scenario 'package-git-marker' $false $false $true
    Invoke-Scenario 'existing-backup' $false $false $false $true
    Invoke-Scenario 'archive-version' $false $false $false $false $true
    Invoke-Scenario 'archive-failure' $false $false $false $false $false $true
    $snapshotField = $service.GetField('_snapshot', [Reflection.BindingFlags]'NonPublic,Static')
    $busyField = $service.GetField('_busy', [Reflection.BindingFlags]'NonPublic,Static')
    $tryBegin = $service.GetMethod('TryBegin', [Reflection.BindingFlags]'NonPublic,Static')
    $statusType = $assembly.GetType('EmpireCraft.Scripts.Diagnostics.EmpireCraftUpdateStatus', $true)
    $snapshot = $snapshotField.GetValue($null)
    foreach ($blocked in @('InstallScheduled', 'InstallPending')) {
        $snapshot.Status = [Enum]::Parse($statusType, $blocked)
        $busyField.SetValue($null, $false)
        $checking = [Enum]::Parse($statusType, 'Checking')
        if ($tryBegin.Invoke($null, @($checking))) {
            throw "The $blocked state allowed another update check."
        }
    }
    Write-Output 'Updater install, archive, rollback, worktree, and duplicate-scheduling scenarios passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureFull) {
        Remove-Item -LiteralPath $fixtureFull -Recurse -Force
    }
}
