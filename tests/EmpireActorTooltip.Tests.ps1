#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$uiHelper = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Components/UIHelper.cs') -Raw
$tooltip = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameLibrary/EmpireCraftTooltipLibrary.cs') -Raw
$clanSystem = Get-Content -LiteralPath (Join-Path $root 'Scripts/GeneralSystems/ClanSystem.cs') -Raw
$actorPatch = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/ActorPatch.cs') -Raw
$historyWindow = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/EmpireHistoryWindow.cs') -Raw
$clanWindow = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/SpecificClanWindow.cs') -Raw
$locales = @(
    Get-Content -LiteralPath (Join-Path $root 'Locales/cz.json') -Raw | ConvertFrom-Json -AsHashtable
    Get-Content -LiteralPath (Join-Path $root 'Locales/ch.json') -Raw | ConvertFrom-Json -AsHashtable
    Get-Content -LiteralPath (Join-Path $root 'Locales/en.json') -Raw | ConvertFrom-Json -AsHashtable
)

if ($uiHelper.Contains('actor.showTooltip(unitLoader)')) {
    throw 'The shared EmpireCraft avatar still invokes the vanilla actor tooltip'
}
if (!$uiHelper.Contains('Tooltip.show(unitLoader, "empirecraft_actor"')) {
    throw 'The shared avatar is not bound to the EmpireCraft actor tooltip'
}
if (!$uiHelper.Contains('actor != null || actor_id > 0 || pIdentity != null')) {
    throw 'Historical avatars cannot open the shared tooltip'
}
if (!$uiHelper.Contains('tip_description = (pIdentity?.id ?? -1L).ToString()') -or
    !$tooltip.Contains('SpecificClanManager.getPerson(identityId)')) {
    throw 'The shared tooltip cannot resolve a directly supplied historical identity'
}
if (!$tooltip.Contains('id = "empirecraft_actor"') -or
    !$tooltip.Contains('callback = showEmpireCraftActor')) {
    throw 'The EmpireCraft actor tooltip asset is not registered'
}
$assetStart = $tooltip.IndexOf('id = "empirecraft_actor"')
$assetEnd = $tooltip.IndexOf('});', $assetStart)
$assetRegistration = $tooltip.Substring($assetStart, $assetEnd - $assetStart)
if (!$assetRegistration.Contains('prefab_id = "tooltips/tooltip_normal"') -or
    $assetRegistration.Contains('prefab_id = "tooltips/tooltip_actor"')) {
    throw 'The EmpireCraft character tooltip still instantiates the vanilla actor UI'
}
$methodStart = $tooltip.IndexOf('private static void showEmpireCraftActor')
$methodEnd = $tooltip.IndexOf('private static PersonalClanIdentity FindIdentityByActorId', $methodStart)
$method = $tooltip.Substring($methodStart, $methodEnd - $methodStart)
if ($method.Contains('showActor(')) { throw 'The custom character tooltip still calls the vanilla actor renderer' }
foreach ($required in @('GetActorAge(actor, identity)', 'GetActorGender(actor, identity)',
        'GetOwnedTitleNames(actor, identity)', 'GetFullOfficeName(actor, identity)',
        'GetPeerageName(actor, identity)', 'GetParentNames(identity)', 'GetSpouseName(identity)',
        'actor?.GetSpecificClan()?.name', 'actor.family.data.name')) {
    if (!$tooltip.Contains($required)) { throw "Missing actor tooltip field: $required" }
}
if (!$tooltip.Contains('FindIdentityByActorId(actorId)') -or
    !$tooltip.Contains('SpecificClanManager._globalPersonLookup.Values.FirstOrDefault')) {
    throw 'Historical actors are not resolved from the persistent genealogy cache'
}
foreach ($snapshot in @('fullOfficeName', 'clanName', 'familyName', 'factionName', 'ownedTitleNames')) {
    if (!$clanSystem.Contains("public string $snapshot") -and
        !$clanSystem.Contains("public List<string> $snapshot")) {
        throw "Missing historical tooltip snapshot: $snapshot"
    }
}
if (!$clanSystem.Contains('public int recordedAge { get; set; } = -1;') -or
    !$clanSystem.Contains('recordedAge = actor.getAge();') -or
    !$clanSystem.Contains('return is_alive && actor != null ? actor.getAge() : recordedAge;')) {
    throw 'Historical character age is not persisted before the live actor disappears'
}
if ($actorPatch.Contains('pci.actor_id = -1L;')) {
    throw 'Dead characters lose the stable actor id required by history avatars'
}
if ($actorPatch.IndexOf('pci.recordAllInfo();') -gt $actorPatch.IndexOf('pci.is_alive = false;')) {
    throw 'Historical tooltip data is captured after the live actor has been invalidated'
}
if (!$historyWindow.Contains('pIdentity: identity') -or !$historyWindow.Contains('legacyMatches.Count == 1')) {
    throw 'Empire history avatars do not support legacy dead-character records'
}
if (([regex]::Matches($clanWindow, 'pIdentity:')).Count -lt 3) {
    throw 'Genealogy avatars do not pass their persistent identity to the shared tooltip'
}
foreach ($locale in $locales) {
    foreach ($key in @('empirecraft_actor_tooltip', 'empirecraft_actor_full_name',
            'empirecraft_actor_age', 'empirecraft_actor_gender',
            'empirecraft_actor_gender_male', 'empirecraft_actor_gender_female',
            'empirecraft_actor_gender_unknown',
            'empirecraft_actor_titles', 'empirecraft_actor_office', 'empirecraft_actor_peerage',
            'empirecraft_actor_specific_clan', 'empirecraft_actor_family',
            'empirecraft_actor_parents', 'empirecraft_actor_spouse')) {
        if ([string]::IsNullOrWhiteSpace($locale[$key])) { throw "Missing localized actor tooltip key: $key" }
    }
}

Write-Output '44 EmpireCraft actor tooltip assertions passed.'
