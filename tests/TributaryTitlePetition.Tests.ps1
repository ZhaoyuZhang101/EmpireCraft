$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Add-Type -Path (Join-Path $root 'Scripts/Layer/TributaryTitlePetitionRules.cs')

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) { throw "$message Expected '$expected', got '$actual'." }
}

[type]$rules = [EmpireCraft.Scripts.Layer.TributaryTitlePetitionRules]
Assert-Equal $true ($rules::CanPetition($true, $false, 0)) 'Zero opinion must be accepted.'
Assert-Equal $false ($rules::CanPetition($true, $false, -1)) 'Negative overlord opinion must reject the petition.'
Assert-Equal $false ($rules::CanPetition($false, $false, 100)) 'Only a tributary may petition.'
Assert-Equal $false ($rules::CanPetition($true, $true, 100)) 'A titled tributary may not petition.'
Assert-Equal 300 $rules::PetitionInfluenceCost 'A title petition must cost exactly 300 influence.'
Assert-Equal $false ($rules::CanPetition($true, $false, 100, 299)) 'Insufficient ruler influence must reject the petition.'
Assert-Equal $true ($rules::CanPetition($true, $false, 100, 300)) 'The exact influence boundary must allow the petition.'
Assert-Equal $true ($rules::ControlsRequiredTerritory($true, 2, 4, 0.5)) 'Control threshold must be inclusive.'
Assert-Equal $false ($rules::ControlsRequiredTerritory($true, 1, 4, 0.5)) 'Insufficient territorial control must fail.'
Assert-Equal $false ($rules::ControlsRequiredTerritory($false, 4, 4, 0.5)) 'The de jure capital must be controlled.'

$empire = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$kingdom = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$actor = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/ActorExtension.cs') -Raw
$plots = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw
$war = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/WarPatch.cs') -Raw
$bureau = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/EmpireBeaurauWindow.cs') -Raw
$relations = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/KingdomTitleRelationResolver.cs') -Raw
$kingdomPatch = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/KingdomPatch.cs') -Raw
$officeCheck = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/ActorAI/EmpireCraftActorCheckOffice.cs') -Raw

Assert-Equal $true ($empire.Contains('getOpinion(CoreKingdom, kingdom)')) 'Opinion direction must be empire toward tributary.'
Assert-Equal $true ($empire.Contains('EmpireCoreManager.GetTitles(core)')) 'Candidates must come from the suzerain imperial core.'
Assert-Equal $true ($empire.Contains('title.title_capital.kingdom != kingdom')) 'The tributary must control the de jure capital.'
Assert-Equal $true ($empire.Contains('GrantTributaryPetitionTitle')) 'The empire must own title granting.'
Assert-Equal $true ($empire.Contains('requestedTitle.owner = ruler;')) 'Granting must repair stale title ownership.'
Assert-Equal $true ($empire.Contains('kingdom.king.renown')) 'Petition candidates must check ruler influence.'
Assert-Equal $true ($empire.Contains('ruler.data.renown -= TributaryTitlePetitionRules.PetitionInfluenceCost;')) 'A successful grant must deduct the shared influence cost.'
Assert-Equal $true ($empire.Contains('SynchronizeLandedLegalTitle(requestedTitle, kingdom);')) 'A granted tributary title must bind its unlanded record to the tributary realm.'
Assert-Equal $true ($empire.Contains('GetLegalPeerageHolder(requestedTitle) != ruler')) 'A grant must verify that the legal holder resolves to the tributary ruler.'
Assert-Equal $true ($empire.Contains('kingdom.GetTakenAllianceEmpire() == this')) 'Landed legal-title resolution must recognize tributary realms.'
Assert-Equal $true ($empire.Contains('bool isPetitionedTributaryTitle = kingdom.GetTakenAllianceEmpire() == this;')) 'Petitioned tributary titles are not distinguished from internal landed grants.'
Assert-Equal $true ($empire.Contains('bool receivesKingPeerage = isImperialClan || isPetitionedTributaryTitle;')) 'A tributary ruler granted a title must receive king rank rather than state-duke rank.'
Assert-Equal $true ($empire.Contains('!IsLandedLegalTitleRealm(landedKingdom)')) 'Landed resolution must accept both imperial members and tributaries.'
Assert-Equal $false ($empire.Contains('landedKingdom.GetEmpire() != this')) 'Landed resolution still rejects tributary realms after accepting them.'
Assert-Equal $true ($bureau.Contains('isImperialClan || isPetitionedTributaryTitle')) 'The imperial administration UI still downgrades a petitioned tributary king to state duke.'
Assert-Equal $true ($relations.Contains('directHolder.kingdom?.GetTakenAllianceEmpire()')) 'The title window cannot display the granting empire for a tributary holder.'
Assert-Equal $true ($kingdomPatch.Contains('__instance.GetEmpire() ?? __instance.GetTakenAllianceEmpire()')) 'A tributary successor does not immediately inherit the petitioned landed peerage.'
Assert-Equal $true ($officeCheck.Contains('pActor.kingdom.GetEmpire() ?? pActor.kingdom.GetTakenAllianceEmpire()')) 'Office repair cannot synchronize a tributary ruler title.'
Assert-Equal $true ($kingdom.Contains('if (kingdom.HasTakenAlliance()) return false;')) 'Ordinary pursuit must reject tributaries.'
Assert-Equal $true ($actor.Contains('if (kingdom.HasTakenAlliance()) return takedTitles;')) 'Direct ordinary title acquisition must reject tributaries.'
Assert-Equal $true ($plots.Contains('id = "kingdom_petition_title"')) 'The tributary petition plot must be registered.'
Assert-Equal $true ($plots.Contains('if (kingdom.HasTakenAlliance()) return false;')) 'Ordinary plot entry points must reject tributaries.'
Assert-Equal $true ($war.Contains('kingdom != null && !kingdom.HasTakenAlliance()')) 'A pending de jure war must not grant a tributary a title.'

Write-Host 'Tributary title petition tests passed (33 assertions).'
