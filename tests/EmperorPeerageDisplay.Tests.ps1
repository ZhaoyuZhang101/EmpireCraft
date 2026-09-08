#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$actorExtension = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/ActorExtension.cs') -Raw
$empire = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$titleWindow = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/KingdomTitleWindow.cs') -Raw
$tooltip = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameLibrary/EmpireCraftTooltipLibrary.cs') -Raw

$peerageStart = $actorExtension.IndexOf('public static string GetPeerageDisplayName')
$peerageMethod = $actorExtension.Substring($peerageStart)
if (!$peerageMethod.Contains('if (a.IsEmperor())')) {
    throw 'Emperor identity is not prioritized by the shared peerage display method'
}
if (!$peerageMethod.Contains('a.SetPeeragesLevel(PeeragesLevel.peerages_0)')) {
    throw 'The shared peerage display method does not repair stale emperor rank data'
}
if (!$peerageMethod.Contains('LM.Get("emperor_suffix")')) {
    throw 'Emperor peerage display does not use the localized emperor suffix'
}
if ($peerageMethod.IndexOf('if (a.IsEmperor())') -gt $peerageMethod.IndexOf('if (a.HasVirtualEnfeoff()')) {
    throw 'Virtual enfeoff data can override the current emperor rank'
}

$identityStart = $empire.IndexOf('//设定天子身份并移居首都')
$identityEnd = $empire.IndexOf('actor.data.renown', $identityStart)
$identityBlock = $empire.Substring($identityStart, $identityEnd - $identityStart)
$officerEnd = $identityBlock.IndexOf("`n        }")
$rankWrite = $identityBlock.IndexOf('actor.SetPeeragesLevel(PeeragesLevel.peerages_0);')
if ($rankWrite -lt $officerEnd) {
    throw 'Emperor rank is still assigned only when the incoming emperor was an officer'
}

if (!$actorExtension.Contains('Empire kingdomEmpire = a.kingdom?.GetEmpire();')) {
    throw 'Old saves cannot recover emperor identity through the core kingdom'
}
if (!$titleWindow.Contains('holder?.GetPeerageDisplayName()')) {
    throw 'The kingdom-title holder card does not use the shared peerage display method'
}
if (!$tooltip.Contains('actor.GetPeerageDisplayName()')) {
    throw 'The EmpireCraft actor tooltip does not use the shared peerage display method'
}

Write-Output '9 emperor peerage display assertions passed.'
