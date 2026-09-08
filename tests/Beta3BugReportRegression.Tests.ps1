#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$plots = Get-Content (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw
$plotCheck = Get-Content (Join-Path $root 'Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckPlots.cs') -Raw
$worldLogs = Get-Content (Join-Path $root 'Scripts/GameLibrary/EmpireCraftWorldLogLibrary.cs') -Raw
$translate = Get-Content (Join-Path $root 'Scripts/HelperFunc/TranslateHelper.cs') -Raw
$laws = Get-Content (Join-Path $root 'Scripts/GameLibrary/EmpireCraftWorldLawLibrary.cs') -Raw

$checks = [ordered]@{
    'Simple plot starter creates a real plot' = $plots.Contains('World.world.plots.newPlot(actor, plotAsset, forced) != null')
    'All four minister plots have explicit starters' = ([regex]::Matches($plots, 'try_to_start_advanced = TryStartSimplePlot').Count -eq 4)
    'Minister scheduler rejects a missing starter' = $plotCheck.Contains('plot?.try_to_start_advanced == null')
    'Religion log writes to religion asset' = $worldLogs.Contains('join_religion_war_log = wl.add(new WorldLogAsset')
    'Rebellion log writes to rebellion asset' = $worldLogs.Contains('join_rebellion_war_log = wl.add(new WorldLogAsset')
    'Rebellion logger rejects an unavailable asset' = $translate.Contains('EmpireCraftWorldLogLibrary.join_rebellion_war_log == null')
    'Social law rejects unavailable world units' = $laws.Contains('World.world?.units == null')
    'Social law skips incomplete actors' = $laws.Contains('a?.asset?.civ == true && a.decisions != null')
    'Social law skips incomplete decisions' = $laws.Contains('if (decision == null) continue;')
}
foreach ($check in $checks.GetEnumerator()) {
    if (-not $check.Value) { throw $check.Key }
}
Write-Output "$($checks.Count) Beta3 bug-report regression assertions passed."
