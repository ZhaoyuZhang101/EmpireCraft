$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Scripts/UI/Components/TerritoryLabelProjection.cs')
$script:assertions = 0
function Assert-True([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:assertions++
}
function Get-Scale([float]$zoom) {
    return [EmpireCraft.Scripts.UI.Components.TerritoryLabelProjection]::FitScale(600 * $zoom, 100 * $zoom, 1024, 160, 0.9, 512)
}
$baseScale = Get-Scale 1
$previous = Get-Scale 0.1
for ($i = 101; $i -le 1100; $i++) {
    $zoom = $i / 1000.0
    $scale = Get-Scale $zoom
    Assert-True ($scale -gt $previous) "Zoom increment at $zoom was quantized away"
    Assert-True ([Math]::Abs($scale - $baseScale * $zoom) -lt 0.000001) "Zoom fit was not continuous at $zoom"
    Assert-True ((1024 + 2) * $scale -le 600 * $zoom + 0.0001) 'Outline exceeds territory width'
    Assert-True ((160 + 2) * $scale -le 100 * $zoom + 0.0001) 'Outline exceeds territory height'
    $previous = $scale
}
Assert-True ((Get-Scale 0.5) -lt (Get-Scale 1)) 'Zoom-out should shrink the same geometry'
$limited = [EmpireCraft.Scripts.UI.Components.TerritoryLabelProjection]::FitScale(100000, 100000, 1024, 160, 0.9, 512)
Assert-True ($limited * 128 -le 512) 'Maximum size exceeded'
Assert-True ([EmpireCraft.Scripts.UI.Components.TerritoryLabelProjection]::FitScale(0, 100, 1024, 160, 0.9, 512) -eq 0) 'Empty territory not hidden'
Assert-True ([EmpireCraft.Scripts.UI.Components.TerritoryLabelProjection]::FitScale(100, 100, 0, 160, 0.9, 512) -eq 0) 'Empty text metrics not hidden'
Assert-True ([EmpireCraft.Scripts.UI.Components.TerritoryLabelProjection]::Visibility(6.0 / 128, 6) -eq 0) 'Minimum visibility not zero'
Assert-True ([Math]::Abs([EmpireCraft.Scripts.UI.Components.TerritoryLabelProjection]::Visibility(7.0 / 128, 6) - 0.5) -lt 0.0001) 'Visibility transition not smooth'
Assert-True ([EmpireCraft.Scripts.UI.Components.TerritoryLabelProjection]::Visibility(8.0 / 128, 6) -eq 1) 'Full visibility not restored'
Write-Output "$script:assertions projection and containment assertions passed."

$renderer = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Components/TerritoryLabelRenderer.cs') -Raw
Assert-True (!$renderer.Contains('Mathf.Round(centerScreen')) 'Position still snaps to whole pixels'
Assert-True (!$renderer.Contains('_text.fontSize = fittedSize')) 'Zoom still rebuilds integer-sized glyphs'
Assert-True ($renderer.Contains('label.RenderSubmitted();')) 'Late camera projection not wired'
Assert-True ($renderer.Contains('overlayCanvas.overridePixelPerfect = true;')) 'Overlay still inherits pixel snapping'
Assert-True ($renderer.Contains('GeometryRefreshInterval = 0.75f;')) 'Territory cache refresh unexpectedly changed'
Write-Output '5 renderer wiring checks passed; Unity rendering is not exercised by this test.'
