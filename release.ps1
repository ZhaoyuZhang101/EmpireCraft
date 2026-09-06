[CmdletBinding()]
param(
    [string]$NotesFile = "RELEASE_NOTES.md",
    [switch]$Draft,
    [switch]$PackageOnly,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

function Invoke-Git {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

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
        $passwordLine = $credentialLines |
            Where-Object { $_ -like "password=*" } |
            Select-Object -First 1

        if ($passwordLine) {
            return $passwordLine.Substring("password=".Length)
        }
    }
    catch {
        # Fall through to an interactive token prompt.
    }

    $secureToken = Read-Host "GitHub token (repo permission required)" -AsSecureString
    if ($secureToken.Length -eq 0) {
        throw "A GitHub token is required."
    }

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function ConvertTo-Utf8JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $json = $Object | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText(
        $Path,
        $json,
        (New-Object System.Text.UTF8Encoding($false))
    )
}

function Invoke-GitHubCurl {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST", "PATCH", "DELETE")]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$Token,

        [object]$JsonBody,

        [string]$InFile,

        [string]$ContentType = "application/json; charset=utf-8",

        [int]$MaxTimeSeconds = 120
    )

    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw "curl.exe is not installed or is not available in PATH."
    }

    $responsePath = [IO.Path]::GetTempFileName()
    $statusPath = [IO.Path]::GetTempFileName()
    $stderrPath = [IO.Path]::GetTempFileName()
    $jsonPath = $null

    try {
        if ($PSBoundParameters.ContainsKey("JsonBody") -and
            -not [string]::IsNullOrWhiteSpace($InFile)) {
            throw "Invoke-GitHubCurl cannot use JsonBody and InFile at the same time."
        }

        $curlArguments = @(
            "--silent",
            "--show-error",
            "--location",
            "--connect-timeout", "15",
            "--max-time", [string]$MaxTimeSeconds,
            "--request", $Method,
            "--header", "Accept: application/vnd.github+json",
            "--header", "Authorization: Bearer $Token",
            "--header", "X-GitHub-Api-Version: 2022-11-28",
            "--header", "User-Agent: EmpireCraft-Release-Script"
        )

        if ($PSBoundParameters.ContainsKey("JsonBody")) {
            $jsonPath = [IO.Path]::GetTempFileName()
            ConvertTo-Utf8JsonFile -Object $JsonBody -Path $jsonPath

            $curlArguments += @(
                "--header", "Content-Type: application/json; charset=utf-8",
                "--data-binary", "@$jsonPath"
            )
        }
        elseif (-not [string]::IsNullOrWhiteSpace($InFile)) {
            if (-not (Test-Path -LiteralPath $InFile -PathType Leaf)) {
                throw "Upload file does not exist: $InFile"
            }

            $curlArguments += @(
                "--header", "Content-Type: $ContentType",
                "--data-binary", "@$InFile"
            )
        }

        $curlArguments += @(
            "--output", $responsePath,
            "--write-out", "%{http_code}",
            $Uri
        )

        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "SilentlyContinue"
            & curl.exe @curlArguments 1> $statusPath 2> $stderrPath
            $curlExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        # Get-Content -Raw returns $null for an empty file in Windows PowerShell.
        # Cast to [string] before Trim() so an empty curl stream does not cause:
        # "You cannot call a method on a null-valued expression."
        $statusText = [string](
            Get-Content -LiteralPath $statusPath -Raw -ErrorAction SilentlyContinue
        )
        $statusText = $statusText.Trim()

        $stderrText = [string](
            Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
        )
        $stderrText = $stderrText.Trim()

        $responseBody = [string](
            Get-Content `
                -LiteralPath $responsePath `
                -Raw `
                -Encoding UTF8 `
                -ErrorAction SilentlyContinue
        )

        if ($curlExitCode -ne 0) {
            $details = if ([string]::IsNullOrWhiteSpace($stderrText)) {
                "curl exit code $curlExitCode"
            }
            else {
                $stderrText
            }

            throw "curl.exe failed while calling GitHub:`n$details"
        }

        $statusCode = 0
        if ([string]::IsNullOrWhiteSpace($statusText)) {
            throw "curl.exe returned no HTTP status code. stderr: $stderrText"
        }

        if (-not [int]::TryParse($statusText, [ref]$statusCode)) {
            throw "GitHub returned an unreadable HTTP status: '$statusText'. stderr: $stderrText"
        }

        if ($statusCode -lt 200 -or $statusCode -ge 300) {
            $friendlyBody = $responseBody

            try {
                if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
                    $errorJson = $responseBody | ConvertFrom-Json
                    $errorLines = @()

                    if ($errorJson.message) {
                        $errorLines += [string]$errorJson.message
                    }

                    if ($errorJson.errors) {
                        foreach ($item in @($errorJson.errors)) {
                            $parts = @()

                            if ($item.resource) { $parts += "resource=$($item.resource)" }
                            if ($item.field)    { $parts += "field=$($item.field)" }
                            if ($item.code)     { $parts += "code=$($item.code)" }
                            if ($item.message)  { $parts += "message=$($item.message)" }

                            if ($parts.Count -gt 0) {
                                $errorLines += ($parts -join ", ")
                            }
                        }
                    }

                    if ($errorLines.Count -gt 0) {
                        $friendlyBody = $errorLines -join [Environment]::NewLine
                    }
                }
            }
            catch {
                # Keep the original response body.
            }

            throw "GitHub API returned HTTP $statusCode for $Method $Uri`n$friendlyBody"
        }

        $jsonResult = $null

        if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
            try {
                $jsonResult = $responseBody | ConvertFrom-Json
            }
            catch {
                # Some successful endpoints have no JSON body.
            }
        }

        return [pscustomobject]@{
            StatusCode = $statusCode
            Body       = $responseBody
            Json       = $jsonResult
        }
    }
    finally {
        Remove-Item `
            -LiteralPath $responsePath, $statusPath, $stderrPath `
            -Force `
            -ErrorAction SilentlyContinue

        if ($jsonPath) {
            Remove-Item -LiteralPath $jsonPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function New-ReleasePackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string]$ArchivePath
    )

    $stageDirectory = Join-Path `
        ([IO.Path]::GetTempPath()) `
        ("EmpireCraft-release-" + [guid]::NewGuid().ToString("N"))

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
        Invoke-Git archive --format=zip "--output=$sourceArchive" HEAD | Out-Null
        Expand-Archive `
            -LiteralPath $sourceArchive `
            -DestinationPath $packageRoot `
            -Force

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

        Compress-Archive `
            -LiteralPath $packageRoot `
            -DestinationPath $ArchivePath `
            -CompressionLevel Optimal
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

    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw "curl.exe is not installed or is not available in PATH."
    }

    $repositoryRoot = (
        Invoke-Git rev-parse --show-toplevel |
        Select-Object -First 1
    ).Trim()

    if ([IO.Path]::GetFullPath($repositoryRoot) -ne
        [IO.Path]::GetFullPath($PSScriptRoot)) {
        throw "Run this script from the EmpireCraft repository root."
    }

    $modJsonPath = Join-Path $repositoryRoot "mod.json"

    if (-not (Test-Path -LiteralPath $modJsonPath -PathType Leaf)) {
        throw "mod.json was not found: $modJsonPath"
    }

    $modInfo = Get-Content `
        -LiteralPath $modJsonPath `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json

    $version = [string]$modInfo.version

    if ([string]::IsNullOrWhiteSpace($version) -or
        $version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') {
        throw "mod.json contains an invalid version: '$version'"
    }

    $assetBaseName = "EmpireCraft_Ver_$version"

    # Existing repository release tags use names such as 0.4.1Beta1,
    # so keep the same convention and do not add a leading "v".
    $tagName = $version

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
    New-ReleasePackage `
        -RepositoryRoot $repositoryRoot `
        -ArchivePath $archivePath

    $archiveSizeMb = [math]::Round(
        (Get-Item -LiteralPath $archivePath).Length / 1MB,
        2
    )

    Write-Host `
        "Package created: $archivePath ($archiveSizeMb MB)" `
        -ForegroundColor Green

    if ($PackageOnly) {
        exit 0
    }

    if ($AllowDirty) {
        throw "-AllowDirty can only be used together with -PackageOnly."
    }

    $branchName = (
        Invoke-Git branch --show-current |
        Select-Object -First 1
    ).Trim()

    if ([string]::IsNullOrWhiteSpace($branchName)) {
        throw "Releases cannot be created from a detached HEAD."
    }

    $remoteUrl = (
        Invoke-Git remote get-url origin |
        Select-Object -First 1
    ).Trim()

    $repositoryMatch = [regex]::Match(
        $remoteUrl,
        'github\.com[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$'
    )

    if (-not $repositoryMatch.Success) {
        throw "The origin remote is not a supported GitHub URL: $remoteUrl"
    }

    $repositorySlug =
        "$($repositoryMatch.Groups['owner'].Value)/$($repositoryMatch.Groups['repo'].Value)"

    Write-Host "Pushing branch $branchName..." -ForegroundColor Cyan
    Invoke-Git push origin $branchName | Out-Host

    # Create the Git tag explicitly before creating the GitHub Release.
    # This makes the tag visible in the local repository and on GitHub even
    # before the Release API call runs.
    $localTag = (Invoke-Git tag --list $tagName | Select-Object -First 1)

    if ([string]::IsNullOrWhiteSpace([string]$localTag)) {
        Write-Host "Creating local git tag $tagName..." -ForegroundColor Cyan
        Invoke-Git tag $tagName | Out-Null
    }
    else {
        Write-Host "Local git tag $tagName already exists." -ForegroundColor Yellow
    }

    $remoteTag = & git ls-remote --tags origin "refs/tags/$tagName" 2>$null

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to query tags from origin."
    }

    if (-not $remoteTag) {
        Write-Host "Pushing git tag $tagName..." -ForegroundColor Cyan
        Invoke-Git push origin $tagName | Out-Host
    }
    else {
        Write-Host "Git tag $tagName already exists on GitHub." -ForegroundColor Yellow
    }

    $isPrerelease = $version -match '(?i)(alpha|beta|preview|rc)'

    $resolvedNotesFile = $null

    if (-not [string]::IsNullOrWhiteSpace($NotesFile)) {
        $candidateNotesFile =
            $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
                $NotesFile
            )

        if (Test-Path -LiteralPath $candidateNotesFile -PathType Leaf) {
            $resolvedNotesFile = $candidateNotesFile
        }
    }

    Write-Host "Using curl.exe for GitHub API..." -ForegroundColor Yellow

    $token = Get-GitHubToken
    $apiUrl = "https://api.github.com/repos/$repositorySlug/releases"

    Write-Host "Checking existing GitHub releases..." -ForegroundColor Cyan

    $releaseListResponse = Invoke-GitHubCurl `
        -Method GET `
        -Uri "${apiUrl}?per_page=100" `
        -Token $token `
        -MaxTimeSeconds 60

    $allReleases = @()

    if ($releaseListResponse.Json) {
        $allReleases = @($releaseListResponse.Json)
    }

    $release = $allReleases |
        Where-Object { $_.tag_name -eq $tagName } |
        Select-Object -First 1

    if ($release) {
        if (-not $release.draft) {
            throw "Release '$tagName' already exists and is already published: $($release.html_url)"
        }

        Write-Host `
            "Existing draft release found; reusing it..." `
            -ForegroundColor Yellow
    }
    else {
        $releaseData = [ordered]@{
            tag_name         = $tagName
            target_commitish = $branchName
            name             = $releaseTitle
            draft            = $true
            prerelease       = [bool]$isPrerelease
        }

        if ($resolvedNotesFile) {
            $releaseData.body = Get-Content `
                -LiteralPath $resolvedNotesFile `
                -Raw `
                -Encoding UTF8
        }
        else {
            $releaseData.generate_release_notes = $true
        }

        Write-Host `
            "Creating GitHub draft release $tagName..." `
            -ForegroundColor Cyan

        $createResponse = Invoke-GitHubCurl `
            -Method POST `
            -Uri $apiUrl `
            -Token $token `
            -JsonBody $releaseData `
            -MaxTimeSeconds 60

        $release = $createResponse.Json

        if (-not $release -or -not $release.id) {
            throw "GitHub created the release but returned no usable release object."
        }
    }

    $archiveFileName = [IO.Path]::GetFileName($archivePath)

    $releaseDetailUrl =
        "https://api.github.com/repos/$repositorySlug/releases/$($release.id)"

    $releaseDetailResponse = Invoke-GitHubCurl `
        -Method GET `
        -Uri $releaseDetailUrl `
        -Token $token `
        -MaxTimeSeconds 60

    if ($releaseDetailResponse.Json) {
        $release = $releaseDetailResponse.Json
    }

    $existingAsset = @($release.assets) |
        Where-Object { $_.name -eq $archiveFileName } |
        Select-Object -First 1

    if ($existingAsset) {
        Write-Host `
            "Removing existing asset $archiveFileName..." `
            -ForegroundColor Yellow

        Invoke-GitHubCurl `
            -Method DELETE `
            -Uri $existingAsset.url `
            -Token $token `
            -MaxTimeSeconds 60 |
            Out-Null
    }

    $uploadBaseUrl =
        $release.upload_url -replace '\{\?name,label\}$', ''

    $assetName = [Uri]::EscapeDataString($archiveFileName)
    $uploadUrl = "${uploadBaseUrl}?name=$assetName"

    Write-Host "Uploading $archiveFileName..." -ForegroundColor Cyan

    $uploadResponse = Invoke-GitHubCurl `
        -Method POST `
        -Uri $uploadUrl `
        -Token $token `
        -InFile $archivePath `
        -ContentType "application/zip" `
        -MaxTimeSeconds 300

    if (-not $uploadResponse.Json -or -not $uploadResponse.Json.id) {
        throw "GitHub did not confirm the uploaded release asset."
    }

    if (-not $Draft) {
        Write-Host "Publishing release..." -ForegroundColor Cyan

        $publishData = [ordered]@{
            draft      = $false
            prerelease = [bool]$isPrerelease
        }

        $publishResponse = Invoke-GitHubCurl `
            -Method PATCH `
            -Uri $releaseDetailUrl `
            -Token $token `
            -JsonBody $publishData `
            -MaxTimeSeconds 60

        if ($publishResponse.Json) {
            $release = $publishResponse.Json
        }
    }

    Write-Host "Release URL: $($release.html_url)" -ForegroundColor Green

    Write-Host "Release completed: $releaseTitle" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "Release failed." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Set-Location $PSScriptRoot
}
