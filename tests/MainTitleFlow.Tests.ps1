#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$kingdom = Get-Content (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$plots = Get-Content (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw
$plotCheck = Get-Content (Join-Path $root 'Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckPlots.cs') -Raw
$kingdomType = Get-Content (Join-Path $root 'Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckKingdomType.cs') -Raw
$dataManager = Get-Content (Join-Path $root 'Scripts/Data/DataManager.cs') -Raw
$actor = Get-Content (Join-Path $root 'Scripts/GameClassExtensions/ActorExtension.cs') -Raw
$culturePatch = Get-Content (Join-Path $root 'Scripts/GamePatches/CulturePatch.cs') -Raw
$kingdomPatch = Get-Content (Join-Path $root 'Scripts/GamePatches/KingdomPatch.cs') -Raw
$regimeWindow = Get-Content (Join-Path $root 'Scripts/UI/Windows/RegimeWindow.cs') -Raw

if (!$kingdom.Contains('public static bool ReconcileMainTitle(')) { throw 'Missing unified main-title reconciliation' }
if (!$kingdom.Contains('KingdomTitle preferred = kingdom.GetCapitalMainTitleCandidate();')) { throw 'Capital title is not first priority' }
if (!$kingdom.Contains('newlyAcquiredTitles.FirstOrDefault(IsUsable)')) { throw 'Acquired title fallback is not wired' }
if ($kingdom.IndexOf('kingdom.setCapital(preferred.title_capital);') -gt $kingdom.IndexOf('kingdom.SetMainTitle(preferred);')) {
    throw 'Title is assigned before its de jure capital migration'
}
if (!$plots.Contains('kingdom.ReconcileMainTitle(titles);')) { throw 'Title acquisition does not reconcile immediately' }
if (!$plotCheck.Contains('pKingdom.ReconcileMainTitle();')) { throw 'Monthly recovery does not reconcile old saves' }
if (!$kingdomType.Contains('KingdomTitle mainTitle = pKingdom.GetMainTitle();')) { throw 'Kingdom naming bypasses validated main-title binding' }
if (!$kingdomType.Contains('regime.type == RegimeType.LvLing && !kingdom.IsEmpire() && kingdom.HasMainTitle()') -or
    !$kingdomType.Contains('return KingdomType.LvLing_kingdom;')) {
    throw 'Hereditary landed LvLing rulers can still be classified as military governorships'
}
if (!$kingdomType.Contains('? mainTitle.name')) { throw 'Validated main title is not the first naming source' }
if (!$kingdomType.Contains(': pKingdom.GetUntitledKingdomName();')) {
    throw 'Kingdom naming does not use the shared unowned-title fallback'
}
if (!$kingdom.Contains('title.owner != ruler && !(title.owner == null && rulerListsTitle)')) {
    throw 'Main-title reads do not reject titles owned by another ruler'
}
if (!$kingdom.Contains('title.owner == king || title.owner == null && ownedTitleIds.Contains(title.id)')) {
    throw 'Main-title reconciliation can still steal another ruler title'
}
if (!$kingdom.Contains('public string initial_random_name = "";') -or
    !$kingdom.Contains('public static string GetInitialRandomKingdomName(this Kingdom kingdom)')) {
    throw 'Initial random kingdom name is not persisted and reusable'
}
if (!$kingdom.Contains('public static string GetUntitledKingdomName(this Kingdom kingdom)') -or
    !$kingdom.Contains('KingdomTitle capitalTitle = kingdom.GetCapitalDeJureTitle();') -or
    !$kingdom.Contains('string capitalName = kingdom.capital.GetCityName();')) {
    throw 'A kingdom without ownership of its capital title does not use the capital city name'
}
if (!$kingdom.Contains('return kingdom.GetInitialRandomKingdomName();')) {
    throw 'A kingdom whose capital has no de jure title does not retain its random name'
}
if (!$kingdom.Substring($kingdom.IndexOf('private static string GetKingdomFrontFallback')).Contains('return kingdom.GetUntitledKingdomName();')) {
    throw 'Generic kingdom-name fallback bypasses the title-aware naming rule'
}
if (!$kingdomPatch.Contains('__instance.RememberInitialRandomKingdomName();') -or
    !$culturePatch.Contains('RememberInitialRandomKingdomName(__instance.kingdom.data.name, overwrite: true);')) {
    throw 'Kingdom creation does not capture the generated random name'
}
if ($actor.Contains('a.kingdom.SetKingdomName(a.kingdom.capital.GetCityName());')) {
    throw 'Losing a title still forcibly renames the kingdom after its capital'
}
if (!$dataManager.Contains('worldKingdom.ReconcileMainTitle();') -or
    !$dataManager.Contains('worldKingdom.GetInitialRandomKingdomName();') -or
    !$dataManager.Contains('EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(worldKingdom);')) {
    throw 'Loaded saves do not repair title binding, random fallback names, and displayed names in order'
}
if (!$dataManager.Contains('AncientWarfareCompatibility.Owns(worldKingdom)')) {
    throw 'Load-time naming repair does not preserve Ancient Warfare ownership'
}
if (([regex]::Matches($regimeWindow, 'RefreshKingdomStatus\(\);')).Count -lt 4 -or
    !$regimeWindow.Contains('EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(_kingdom);')) {
    throw 'Regime option changes do not immediately refresh kingdom type and name'
}

Write-Output '25 main-title ownership and naming assertions passed.'
