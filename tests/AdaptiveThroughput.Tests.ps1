#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$rules = Get-Content -LiteralPath (Join-Path $root 'Scripts/HelperFunc/EmpireCraftFrameSchedulingRules.cs') -Raw
$scheduler = Get-Content -LiteralPath (Join-Path $root 'Scripts/GeneralSystems/EmpireCraftStrategicScheduler.cs') -Raw
$titles = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/KingdomTtitleManager.cs') -Raw
$configActions = Get-Content -LiteralPath (Join-Path $root 'Scripts/Data/ConfigActions.cs') -Raw
$modClass = Get-Content -LiteralPath (Join-Path $root 'Scripts/ModClass.cs') -Raw
$config = Get-Content -LiteralPath (Join-Path $root 'default_config.json') -Raw | ConvertFrom-Json -AsHashtable
$locales = @(
    Get-Content -LiteralPath (Join-Path $root 'Locales/cz.json') -Raw | ConvertFrom-Json -AsHashtable
    Get-Content -LiteralPath (Join-Path $root 'Locales/ch.json') -Raw | ConvertFrom-Json -AsHashtable
    Get-Content -LiteralPath (Join-Path $root 'Locales/en.json') -Raw | ConvertFrom-Json -AsHashtable
)

$assertions = 0
function Assert-Contains([string]$text, [string]$needle, [string]$message) {
    if (!$text.Contains($needle)) { throw $message }
    $script:assertions++
}

Assert-Contains $modClass 'PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE = true' 'Adaptive throughput has no runtime switch'
Assert-Contains $configActions 'AdaptiveThroughputPerformanceCallBack(bool on)' 'Adaptive throughput config callback is missing'
Assert-Contains $configActions 'EmpireCraftStrategicScheduler.Reset()' 'Changing throughput mode does not reset scheduler state'
Assert-Contains $rules 'ResolveMaximumEmpires(bool adaptiveThroughput' 'Adaptive empire batch rule is missing'
Assert-Contains $rules 'double frameMilliseconds)' 'Adaptive title batch rule does not receive frame timing'
Assert-Contains $rules 'frameMilliseconds <= SpareFrameThresholdMilliseconds' 'Spare frame time is not detected'
Assert-Contains $scheduler 'budgetMilliseconds * EmpireBudgetShare' 'Empire work has no bounded share of the frame budget'
Assert-Contains $scheduler 'int processedEmpires = 0' 'Scheduler still processes exactly one empire per frame'
Assert-Contains $scheduler 'PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE' 'Scheduler ignores adaptive throughput switch'
Assert-Contains $titles 'Time.unscaledDeltaTime * 1000d' 'Title maintenance does not reduce its batch on busy frames'

$setting = $config.Default | Where-Object Id -eq 'performance_adaptive_throughput_toggle'
if ($null -eq $setting -or !$setting.BoolVal -or
    $setting.Callback -ne 'ConfigActions:AdaptiveThroughputPerformanceCallBack') {
    throw 'Adaptive throughput setting is missing, disabled by default, or connected to the wrong callback'
}
$assertions++

foreach ($locale in $locales) {
    foreach ($key in @('performance_adaptive_throughput_toggle',
            'performance_adaptive_throughput_toggle Description')) {
        if ([string]::IsNullOrWhiteSpace($locale[$key])) { throw "Missing localized performance key: $key" }
        $assertions++
    }
}

Add-Type -Path (Join-Path $root 'Scripts/HelperFunc/EmpireCraftFrameSchedulingRules.cs')
$ruleType = [EmpireCraft.Scripts.HelperFunc.EmpireCraftFrameSchedulingRules]
if ($ruleType::ResolveBudgetMilliseconds($true, $true, 20000, 16d) -ne 3.5d) {
    throw 'Extreme-population spare frames do not receive the expected throughput budget'
}
$assertions++
if ($ruleType::ResolveBudgetMilliseconds($true, $true, 20000, 40d) -ne 0.5d) {
    throw 'Busy frames do not fall back to the minimum safe budget'
}
$assertions++
if ($ruleType::ResolveMaximumKingdoms($true, $true, 20000) -le
    $ruleType::ResolveMaximumKingdoms($true, $false, 20000)) {
    throw 'Adaptive mode does not expand the kingdom batch ceiling'
}
$assertions++
if ($ruleType::ResolveMaximumTitles($true, $true, 20000, 30d) -ne
    $ruleType::ResolveMaximumTitles($true, $false, 20000, 30d)) {
    throw 'Busy frames do not restore the conservative title batch ceiling'
}
$assertions++

Write-Output "$assertions adaptive throughput assertions passed."
