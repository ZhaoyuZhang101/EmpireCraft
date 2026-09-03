[CmdletBinding()]
param(
    [string]$NotesFile = "RELEASE_NOTES.md",
    [switch]$Draft,
    [switch]$PackageOnly,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    # Capture native streams in files because Windows PowerShell otherwise turns
    # normal Git stderr messages such as "Everything up-to-date" into errors.
    $stdoutPath = [IO.Path]::GetTempFileName()
    $stderrPath = [IO.Path]::GetTempFileName()
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "SilentlyContinue"
        & git @Arguments 1> $stdoutPath 2> $stderrPath
        $exitCode = $LASTEXITCODE
        $output = @()
        $output += @(Get-Content -LiteralPath $stdoutPath -ErrorAction SilentlyContinue)
        $output += @(Get-Content -LiteralPath $stderrPath -ErrorAction SilentlyContinue)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }

    if ($exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

function Get-GitHubToken {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        return $env:GITHUB_TOKEN.Trim()
    }

    try {
        $credentialLines = "protocol=https`nhost=github.com`n`n" | & git credential fill 2>$null
        $passwordLine = $credentialLines | Where-Object { $_ -like "password=*" } | Select-Object -First 1
        if ($passwordLine) {
            return $passwordLine.Substring("password=".Length)
        }
    }
    catch {
        # Fall through to an interactive token prompt.
    }

    $secureToken = Read-Host "GitHub token (repo permission required)" -AsSecureString
    if ($secureToken.Length -eq 0) {
        throw "A GitHub token is required when GitHub CLI is unavailable."
    }

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function New-ReleasePackage {
    param(
        [string]$RepositoryRoot,
        [string]$ArchivePath
    )

    $stageDirectory = Join-Path ([IO.Path]::GetTempPath()) ("EmpireCraft-release-" + [guid]::NewGuid().ToString("N"))
    $packageRoot = Join-Path $stageDirectory "EmpireCraft"
    $sourceArchive = Join-Path $stageDirectory "repository.zip"
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

    $excludedPrefixes = @(
        ".agents/",
        ".github/",
        ".idea/",
        "bin/",
        "dist/",
        "obj/",
        "RegimeEditor/"
    )
    $excludedFiles = @(
        ".gitignore",
        "EmpireCraft.csproj",
        "EmpireCraft.sln",
        "EmpireCraft.sln.DotSettings.user",
        "release.cmd",
        "release.ps1",
        "RELEASE_NOTES.md"
    )

    try {
        # Let Git write path bytes directly into an archive so Windows PowerShell
        # never has to decode tracked file names containing Chinese characters.
        Invoke-Git archive --format=zip "--output=$sourceArchive" HEAD | Out-Null
        Expand-Archive -LiteralPath $sourceArchive -DestinationPath $packageRoot -Force

        foreach ($prefix in $excludedPrefixes) {
            $relativeDirectory = $prefix.TrimEnd("/").Replace("/", "\")
            $excludedPath = Join-Path $packageRoot $relativeDirectory
            if (Test-Path -LiteralPath $excludedPath) {
                Remove-Item -LiteralPath $excludedPath -Recurse -Force
            }
        }
        foreach ($relativeFile in $excludedFiles) {
            $excludedPath = Join-Path $packageRoot $relativeFile
            if (Test-Path -LiteralPath $excludedPath) {
                Remove-Item -LiteralPath $excludedPath -Force
            }
        }

        if (Test-Path -LiteralPath $ArchivePath) {
            Remove-Item -LiteralPath $ArchivePath -Force
        }
        Compress-Archive -LiteralPath $packageRoot -DestinationPath $ArchivePath -CompressionLevel Optimal
    }
    finally {
        if (Test-Path -LiteralPath $stageDirectory) {
            Remove-Item -LiteralPath $stageDirectory -Recurse -Force
        }
    }
}

try {
    Set-Location $PSScriptRoot

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is not installed or is not available in PATH."
    }

    $repositoryRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
    if ([IO.Path]::GetFullPath($repositoryRoot) -ne [IO.Path]::GetFullPath($PSScriptRoot)) {
        throw "Run this script from the EmpireCraft repository root."
    }

    $modJsonPath = Join-Path $repositoryRoot "mod.json"
    $modInfo = Get-Content -LiteralPath $modJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $version = [string]$modInfo.version
    if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') {
        throw "mod.json contains an invalid version: '$version'"
    }

    $assetBaseName = "EmpireCraft_Ver_$version"
    $tagName = "v$version"
    $releaseTitle = "EmpireCraft Ver $version"
    $distDirectory = Join-Path $repositoryRoot "dist"
    $archivePath = Join-Path $distDirectory "$assetBaseName.zip"
    New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null

    if (-not $AllowDirty) {
        $changes = @(Invoke-Git status --porcelain --untracked-files=all)
        if ($changes.Count -gt 0) {
            throw "The working tree is not clean. Commit or stash changes before releasing.`n$($changes -join [Environment]::NewLine)"
        }
    }

    Write-Host "Packaging $assetBaseName..." -ForegroundColor Cyan
    New-ReleasePackage -RepositoryRoot $repositoryRoot -ArchivePath $archivePath
    $archiveSizeMb = [math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 2)
    Write-Host "Package created: $archivePath ($archiveSizeMb MB)" -ForegroundColor Green

    if ($PackageOnly) {
        exit 0
    }

    if ($AllowDirty) {
        throw "-AllowDirty can only be used together with -PackageOnly."
    }

    $branchName = (Invoke-Git branch --show-current | Select-Object -First 1).Trim()
    if ([string]::IsNullOrWhiteSpace($branchName)) {
        throw "Releases cannot be created from a detached HEAD."
    }

    $remoteUrl = (Invoke-Git remote get-url origin | Select-Object -First 1).Trim()
    $repositoryMatch = [regex]::Match($remoteUrl, 'github\.com[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$')
    if (-not $repositoryMatch.Success) {
        throw "The origin remote is not a supported GitHub URL: $remoteUrl"
    }
    $repositorySlug = "$($repositoryMatch.Groups['owner'].Value)/$($repositoryMatch.Groups['repo'].Value)"

    $existingTag = & git ls-remote --tags origin "refs/tags/$tagName" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to query tags from origin."
    }
    if ($existingTag) {
        throw "Tag $tagName already exists on GitHub. Update mod.json before releasing again."
    }

    Write-Host "Pushing branch $branchName..." -ForegroundColor Cyan
    Invoke-Git push origin $branchName | Out-Host

    $isPrerelease = $version -match '(?i)(alpha|beta|preview|rc)'
    $resolvedNotesFile = $null
    if (-not [string]::IsNullOrWhiteSpace($NotesFile)) {
        $candidateNotesFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($NotesFile)
        if (Test-Path -LiteralPath $candidateNotesFile -PathType Leaf) {
            $resolvedNotesFile = $candidateNotesFile
        }
    }

    $ghCommand = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCommand) {
        $arguments = @(
            "release", "create", $tagName, $archivePath,
            "--repo", $repositorySlug,
            "--target", $branchName,
            "--title", $releaseTitle
        )
        if ($resolvedNotesFile) {
            $arguments += @("--notes-file", $resolvedNotesFile)
        }
        else {
            $arguments += "--generate-notes"
        }
        if ($isPrerelease) {
            $arguments += "--prerelease"
        }
        if ($Draft) {
            $arguments += "--draft"
        }

        Write-Host "Creating GitHub release with GitHub CLI..." -ForegroundColor Cyan
        & $ghCommand.Source @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub CLI failed to create the release."
        }
    }
    else {
        Write-Host "GitHub CLI not found; using the GitHub API..." -ForegroundColor Yellow
        $token = Get-GitHubToken
        $headers = @{
            Accept = "application/vnd.github+json"
            Authorization = "Bearer $token"
            "X-GitHub-Api-Version" = "2022-11-28"
            "User-Agent" = "EmpireCraft-Release-Script"
        }

        $releaseData = @{
            tag_name = $tagName
            target_commitish = $branchName
            name = $releaseTitle
            draft = $true
            prerelease = [bool]$isPrerelease
        }
        if ($resolvedNotesFile) {
            $releaseData.body = Get-Content -LiteralPath $resolvedNotesFile -Raw -Encoding UTF8
        }
        else {
            $releaseData.generate_release_notes = $true
        }

        $apiUrl = "https://api.github.com/repos/$repositorySlug/releases"
        $release = Invoke-RestMethod -Method Post -Uri $apiUrl -Headers $headers -ContentType "application/json; charset=utf-8" -Body ($releaseData | ConvertTo-Json)
        $uploadBaseUrl = $release.upload_url -replace '\{\?name,label\}$', ''
        $assetName = [Uri]::EscapeDataString([IO.Path]::GetFileName($archivePath))
        Invoke-RestMethod -Method Post -Uri "${uploadBaseUrl}?name=$assetName" -Headers $headers -ContentType "application/zip" -InFile $archivePath | Out-Null
        if (-not $Draft) {
            $publishUrl = "https://api.github.com/repos/$repositorySlug/releases/$($release.id)"
            $release = Invoke-RestMethod -Method Patch -Uri $publishUrl -Headers $headers -ContentType "application/json; charset=utf-8" -Body (@{ draft = $false } | ConvertTo-Json)
        }
        Write-Host "Release URL: $($release.html_url)" -ForegroundColor Green
    }

    & git fetch origin "refs/tags/$tagName`:refs/tags/$tagName" 2>$null | Out-Null
    Write-Host "Release completed: $releaseTitle" -ForegroundColor Green
}
catch {
    Write-Host "Release failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Set-Location $PSScriptRoot
}
