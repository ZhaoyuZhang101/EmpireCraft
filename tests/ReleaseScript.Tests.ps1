$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$release = Get-Content -LiteralPath (Join-Path $root 'release.ps1') -Raw

$checks = [ordered]@{
    'Upload progress is visible' = $release.Contains('[switch]$ShowProgress') -and
        $release.Contains('"--progress-bar"') -and $release.Contains('-ShowProgress')
    'Uploads avoid HTTP2 proxy stalls' = $release.Contains('"--http1.1"')
    'Uploads retry transient failures' = $release.Contains('"--retry", "3"') -and
        $release.Contains('"--retry-connrefused"')
    'Uploads stop when traffic stalls' = $release.Contains('"--speed-limit", "1024"') -and
        $release.Contains('"--speed-time", "30"')
    'Expect handshake is disabled' = $release.Contains('"--header", "Expect:"')
    'Development tests are not packaged' = $release.Contains('"tests/"')
}

foreach ($check in $checks.GetEnumerator()) {
    if (-not $check.Value) { throw $check.Key }
}

Write-Output "$($checks.Count) release upload and packaging assertions passed."
