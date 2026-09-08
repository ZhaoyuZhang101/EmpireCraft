#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tooltip = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameLibrary/EmpireCraftTooltipLibrary.cs') -Raw
$relations = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/KingdomTitleRelationResolver.cs') -Raw
$start = $tooltip.IndexOf('public static void showKingdomTitleToolTip')
$end = $tooltip.IndexOf('public static void showEmpireCoreToolTip', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Kingdom title tooltip method boundaries are missing' }
$method = $tooltip.Substring($start, $end - $start)

foreach ($required in @('"province_name"', '"kingdom_title_capital"',
        'KingdomTitleRelationResolver.ResolveCurrentHolders(title)',
        'KingdomTitleRelationResolver.FindCurrentAdministrations(title)',
        '"kingdom_title_administrator"', '"kingdom_title_landed_holder"',
        '"kingdom_title_unlanded_holder"', '"kingdom_title_controller"',
        'FormatAdministrationRulers(', 'FormatTitleHolders(holders, false)',
        'FormatTitleHolders(holders, true)')) {
    if (!$method.Contains($required)) { throw "Kingdom title tooltip is missing: $required" }
}
foreach ($required in @('GetLegalPeerageHolder(title)', 'GetLandedLegalTitleKingdom(title)',
        'directHolder', 'Unlanded = landedKingdom == null', 'GetAdministrativeTitle() == title')) {
    if (!$relations.Contains($required)) { throw "Shared title relation resolver is missing: $required" }
}

foreach ($file in @('Locales/cz.json', 'Locales/ch.json', 'Locales/en.json')) {
    $locale = Get-Content -LiteralPath (Join-Path $root $file) -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in @('province_name', 'kingdom_title_capital', 'kingdom_title_administrator',
            'kingdom_title_landed_holder', 'kingdom_title_unlanded_holder', 'kingdom_title_controller')) {
        if ([string]::IsNullOrWhiteSpace($locale[$key])) { throw "$file is missing $key" }
    }
}

Write-Output '35 kingdom title tooltip assertions passed.'
