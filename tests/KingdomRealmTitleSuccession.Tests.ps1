$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$kingdom = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$actor = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/ActorExtension.cs') -Raw
$kingdomPatch = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/KingdomPatch.cs') -Raw
$actorPatch = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/ActorPatch.cs') -Raw
$dataManager = Get-Content -LiteralPath (Join-Path $root 'Scripts/Data/DataManager.cs') -Raw

if (!$kingdom.Contains('public List<long> RealmTitles = new List<long>();')) {
    throw 'Kingdom de jure titles are not persisted independently from the ruler'
}
if (!$kingdom.Contains('public static bool RegisterRealmTitle(this Kingdom kingdom, KingdomTitle title)')) {
    throw 'Kingdom title registration is missing'
}
if (!$kingdom.Contains('kingdom.IsTitleWithinRealm(title)')) {
    throw 'Personal titles outside the realm can become kingdom titles'
}
if (!$kingdom.Contains('public static void TransferRealmTitlesToRuler(this Kingdom kingdom, Actor ruler)')) {
    throw 'Kingdom title succession is missing'
}
if (!$kingdom.Contains('return kingdom.GetRealmTitleIds()') -or
    !$kingdom.Contains('.Select(ModClass.KINGDOM_TITLE_MANAGER.get)')) {
    throw 'Kingdom title queries still expose only the single main title'
}
if (!$kingdom.Contains('title.owner.GetOwnedTitle()?.Remove(title.id);') -or
    !$kingdom.Contains('ruler.AddOwnedTitle(title);')) {
    throw 'Established titles are not transferred from the previous holder to the new ruler'
}
if (!$actor.Contains('a.kingdom.RegisterRealmTitle(title);')) {
    throw 'Titles acquired by a reigning king are not registered to the kingdom'
}
if (!$actor.Contains('a.kingdom.UnregisterRealmTitle(title);')) {
    throw 'Titles explicitly lost by a reigning king remain registered to the kingdom'
}
if (!$kingdomPatch.Contains('prefix: new HarmonyMethod(GetType(), nameof(before_new_emperor))')) {
    throw 'Deposition/election does not capture the outgoing ruler titles before setKing'
}
if (!$kingdomPatch.Contains('__instance.TransferRealmTitlesToRuler(pActor);')) {
    throw 'A newly installed ruler does not receive all kingdom titles'
}
if (!$actorPatch.Contains('rulingKingdom?.SyncRealmTitlesFromRuler(__instance);') -or
    !$actorPatch.Contains('realmTitleIds.Contains(titleID)')) {
    throw 'A ruler death can erase the kingdom title registry when no designated heir exists'
}
if (!$dataManager.Contains('worldKingdom.SyncRealmTitlesFromRuler();') -or
    !$dataManager.Contains('worldKingdom.TransferRealmTitlesToRuler(worldKingdom.king);')) {
    throw 'Old saves do not recover kingdom titles from their current ruler'
}

Write-Output '12 kingdom realm-title succession assertions passed.'
