$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Assert-Contains([string]$source, [string]$needle, [string]$message) {
    if (!$source.Contains($needle)) { throw $message }
}

function Assert-NotContains([string]$source, [string]$needle, [string]$message) {
    if ($source.Contains($needle)) { throw $message }
}

$plots = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw
$kingdom = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$faction = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/FixedFaction.cs') -Raw
$regime = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/RegimeSystem.cs') -Raw
$cabinet = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireAI/EmpireCraftEmpireBehCheckCabinet.cs') -Raw
$office = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/OfficeSystem.cs') -Raw
$detail = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/FactionDetailWindow.cs') -Raw
$ui = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Components/UIHelper.cs') -Raw

$targetStart = $plots.IndexOf('private static Kingdom FindLocalFactionInfluenceTarget')
$targetEnd = $plots.IndexOf('private static void GuardAllPlotAssets', $targetStart)
$targetMethod = $plots.Substring($targetStart, $targetEnd - $targetStart)
Assert-Contains $targetMethod 'return core != null && !core.isRekt() && core.GetEmpire() == empire ? core : null;' 'Local faction action must target the imperial core.'
Assert-NotContains $targetMethod 'target != empire.CoreKingdom' 'The imperial core is still excluded from local influence.'
Assert-Contains $plots 'target.TryIncreaseFactionRatio(pActor.GetFaction(), 1)' 'The local plot does not increase central faction share.'

Assert-Contains $kingdom 'public static void ReconcileFactionRatios' 'Central ratios are not preserved across faction clones.'
Assert-Contains $kingdom 'if (k.IsEmpire()) k.ReconcileFactionRatios(factions);' 'Regime loading does not restore direct-domain ratios.'
Assert-Contains $office 'if (k == empire.CoreKingdom)' 'Office initialization does not distinguish the direct domain.'
Assert-Contains $office 'k.ReconcileFactionRatios(pKingdom.GetRegime().GetPlayerFactions());' 'Office initialization still clears central ratios.'

Assert-Contains $faction 'public int CentralRatio => Empire?.CoreKingdom?.GetFactionRatioValue(this) ?? 0;' 'Faction influence is not sourced from the central ratio.'
Assert-Contains $faction 'public int TotalPower => CentralRatio;' 'Legacy total power still sums member performance.'
Assert-Contains $regime 'OrderByDescending(faction => faction.CentralRatio)' 'Dominant faction is not selected by central share.'
Assert-Contains $regime 'kingdom.ReconcileFactionRatios(PlayerFactions);' 'Recovering faction configuration discards central shares.'
Assert-Contains $regime 'PlayerFactions.ForEach(faction => faction.Update());' 'Recovered factions do not immediately restore leaders.'
Assert-Contains $faction 'candidate ??= Empire.getUnits().Where(actor => IsEligibleLeader(actor) && !actor.HasFaction()' 'Leader fallback does not recruit an eligible imperial actor.'
Assert-Contains $faction 'member.GetFaction() != this' 'Invalid faction members are not removed before leader selection.'
Assert-Contains $faction 'newLeader.SetFaction(this);' 'An appointed leader is not guaranteed to belong to the faction.'
Assert-Contains $cabinet 'ff.Update();' 'Faction leaders are not repaired during imperial cabinet updates.'

Assert-Contains $detail 'LM.Get("label_central_ratio")' 'Faction detail still labels member power instead of central share.'
Assert-Contains $detail '_faction.CentralRatio}%' 'Faction detail does not show a percentage.'
Assert-Contains $ui 'faction.CentralRatio}%' 'Faction cards do not show a percentage.'
Assert-NotContains $ui '综合力量：{faction.TotalPower}' 'Faction cards still display aggregate member power.'

foreach ($localeName in 'cz.json', 'ch.json', 'en.json') {
    $locale = Get-Content -LiteralPath (Join-Path $root "Locales/$localeName") -Raw |
        ConvertFrom-Json -AsHashtable
    if (!$locale.ContainsKey('label_central_ratio') -or [string]::IsNullOrWhiteSpace($locale['label_central_ratio'])) {
        throw "$localeName is missing label_central_ratio"
    }
}

Write-Output '23 central faction influence and leadership assertions passed.'
