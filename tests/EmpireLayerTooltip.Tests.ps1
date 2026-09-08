#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tooltip = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameLibrary/EmpireCraftTooltipLibrary.cs') -Raw
$metaType = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameLibrary/EmpireCraftMetaTypeLibrary.cs') -Raw
$start = $tooltip.IndexOf('public static void showEmpireToolTip')
$end = $tooltip.IndexOf('public static void showKingdomTitleToolTip', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Empire tooltip method boundaries are missing' }
$method = $tooltip.Substring($start, $end - $start)

if (!$method.Contains('stats.gameObject.SetActive(false)')) { throw 'Vanilla empire statistics row is still visible' }
foreach ($removed in @('CountPopulation()', 'countAdults()', 'countChildren()', 'countZones()', 'countHoused()',
        'setIconValue(pTooltip')) {
    if ($method.Contains($removed)) { throw "Expensive vanilla statistic remains in empire tooltip: $removed" }
}
if (!$method.Contains('regime?.HasEraName() == true && pEmpire.HasYearName()') -or
    !$method.Contains('pEmpire.GetYearNameWithTime()')) {
    throw 'Era names are not gated by the active regime or do not include the reign year'
}
foreach ($required in @('EmpireCoreManager.Get(pEmpire)', 'GetAscensionTitleName(pEmpire, core)',
        'pEmpire.CurrentMoney.ToString()', 'pEmpire.Mandate.ToString()', 'regime?.GetDominateFaction()',
        'GetRunningClaim(pEmpire, regime)', 'claim.ShowAsPlot ? LM.Get("tf_starting")',
        '"current_selected_province", tKingdom.GetKingdomName()', 'tKingdom.king?.getName()',
        'LM.Get(tKingdom.GetKingdomType().ToString())', 'GetOwnedTitleNames(tKingdom.king, null)')) {
    if (!$method.Contains($required)) { throw "Missing EmpireCraft tooltip field: $required" }
}

foreach ($required in @('pZone?.city?.kingdom', 'ShowEmpireCursorTooltip(hoveredEmpire',
        'kingdom = tooltipKingdom', 'tip_description = empireMeta.id.ToString()')) {
    if (!$metaType.Contains($required)) { throw "Empire layer does not preserve hovered province context: $required" }
}
if (!$method.Contains('long.TryParse(pData.tip_description, out explicitEmpireId)')) {
    throw 'Empire tooltip does not preserve the layer-resolved empire for tributary views'
}
if ($metaType.Contains('pAsset13.check_cursor_tooltip = new MetaZoneTooltipAction(checkCursorTooltipDefault);')) {
    throw 'Empire layer still uses the context-dropping default tooltip callback'
}

foreach ($file in @('Locales/cz.json', 'Locales/ch.json', 'Locales/en.json')) {
    $locale = Get-Content -LiteralPath (Join-Path $root $file) -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in @('empire_tooltip_core', 'empire_tooltip_ascension_title',
            'empire_tooltip_running_claim', 'empire_tooltip_claim_format',
            'empire_tooltip_province_ruler', 'empire_tooltip_province_type',
            'empire_tooltip_province_titles')) {
        if ([string]::IsNullOrWhiteSpace($locale[$key])) { throw "$file is missing $key" }
    }
}

Write-Output '31 empire layer tooltip assertions passed.'
