$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$metaPath = Join-Path $root 'Scripts/GameLibrary/EmpireCraftMetaTypeLibrary.cs'
$corePath = Join-Path $root 'Scripts/Layer/CoreEmpireTitleManager.cs'
$labelPath = Join-Path $root 'Scripts/UI/Components/TerritoryLabelRenderer.cs'
$meta = Get-Content -LiteralPath $metaPath -Raw
$core = Get-Content -LiteralPath $corePath -Raw
$labels = Get-Content -LiteralPath $labelPath -Raw
$script:assertions = 0

function Assert-True([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:assertions++
}

Assert-True (!$meta.Contains('drawDefaultMeta(kingdom.meta_type_asset);')) `
    'Empire layer redraws the entire world once per independent kingdom'
Assert-True (!$meta.Contains('drawDefaultMeta(city.meta_type_asset);')) `
    'Law layer redraws the entire world once per untitled city'
Assert-True ($meta.Contains('drawEmpireViewCities(pEmpire, kingdom.cities);')) `
    'Tributary layer still builds allocation-heavy Union lists'
Assert-True (($meta | Select-String -Pattern 'Date\.getMonthsSince\(_last_dynamic_zones_ts\) < 1' -AllMatches).Matches.Count -ge 2) `
    'Both custom map layers must throttle full-population dynamic-zone scans'
Assert-True (!$core.Contains('return GetTitles(core).Contains(title);')) `
    'Per-zone title membership still allocates a complete title list'
Assert-True ($labels.Contains('SubmitEmpireCore($"law-empire:{core.id}"')) `
    'Law labels still eagerly allocate a city list every frame'

Write-Output "$script:assertions map-layer performance assertions passed."
