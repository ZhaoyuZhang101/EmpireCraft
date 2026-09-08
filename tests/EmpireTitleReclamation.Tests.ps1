#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$empire = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$plots = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw

$start = $empire.IndexOf('public int ReclaimTitles(')
$end = $empire.IndexOf('public KingdomTitle GetTributaryPetitionTitle', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Empire title-reclamation method boundaries are missing' }
$method = $empire.Substring($start, $end - $start)

foreach ($required in @('sourceKingdom.GetOrCreate().MainTitle', 'sourceKingdom.RemoveMainTitle();',
        'formerHolder?.GetOwnedTitle()?.Remove(title.id)', 'sourceKingdom.UnregisterRealmTitle(title)',
        'title.main_kingdom = null', 'title.EndJurisdiction(sourceKingdom',
        'data.legal_peerage_holders?.Remove(title.id)',
        'data.legal_peerage_holder_identities?.Remove(title.id)',
        'data.legal_peerage_types?.Remove(title.id)', 'data.legal_peerage_kingdoms?.Remove(title.id)',
        'Emperor.AddOwnedTitle(title)', 'title.owner = Emperor',
        'EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(sourceKingdom)')) {
    if (!$method.Contains($required)) { throw "Incomplete imperial title reclamation: $required" }
}
if ($method.IndexOf('sourceKingdom.RemoveMainTitle();') -gt $method.IndexOf('Emperor.AddOwnedTitle(title)')) {
    throw 'The kingdom main title is cleared after ownership has already moved to the emperor'
}
if (!$plots.Contains('empire.ReclaimTitles(k, kingdomTitles)')) {
    throw 'The imperial reclamation plot bypasses the unified transfer operation'
}
if ($plots.Contains('kingdomTitles?.ForEach(pActor.AddOwnedTitle)')) {
    throw 'The old duplicate-only title transfer remains active'
}
if (!$empire.Contains('Emperor != null && title.owner == Emperor &&') -or
    !$empire.Contains('Emperor.GetOwnedTitle()?.Contains(title.id) == true')) {
    throw 'Reclaimed titles can still be immediately re-enfeoffed by automatic succession'
}

Write-Output '18 imperial title-reclamation assertions passed.'
