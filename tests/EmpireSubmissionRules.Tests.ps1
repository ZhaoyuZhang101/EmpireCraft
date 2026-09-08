$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Add-Type -Path (Join-Path $root 'Scripts/Layer/EmpireSubmissionRules.cs')

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) {
        throw "$message Expected '$expected', got '$actual'."
    }
}

[type]$rules = [EmpireCraft.Scripts.Layer.EmpireSubmissionRules]

Assert-Equal $false ($rules::CanAcceptVoluntarySubmission(79, 100)) 'Mandate below 80 must reject submission.'
Assert-Equal $true ($rules::CanAcceptVoluntarySubmission(80, 0)) 'Boundary values must allow submission.'
Assert-Equal $false ($rules::CanAcceptVoluntarySubmission(100, -1)) 'Negative treasury must reject submission.'
Assert-Equal $true ($rules::CanAcceptVoluntarySubmission(100, 1)) 'Healthy empires must allow submission.'
Assert-Equal $true ($rules::CanVoluntarilyBecomeTributary($false, $false)) 'A titleless kingdom may become a tributary.'
Assert-Equal $true ($rules::CanVoluntarilyBecomeTributary($true, $false)) 'A kingdom from another de jure core may become a tributary.'
Assert-Equal $false ($rules::CanVoluntarilyBecomeTributary($true, $true)) 'A titled kingdom within the overlord core must not become a tributary voluntarily.'

$origins = [System.Collections.Generic.List[long]]::new()
$origins.Add(12)
$origins.Add(37)
Assert-Equal $true ($rules::IsOriginalRebellionEmpire($origins, 12)) 'Original empire must be remembered.'
Assert-Equal $false ($rules::IsOriginalRebellionEmpire($origins, 13)) 'Other empires must remain eligible.'
Assert-Equal $false ($rules::IsOriginalRebellionEmpire($null, 12)) 'Missing legacy save data must be safe.'
Assert-Equal -100 $rules::RebellionOpinionPenalty 'Rebellion opinion penalty must be exactly -100.'

$empireSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$kingdomSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$plotSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw
$opinionSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireCraftOpinionAddition.cs') -Raw
$cityPatchSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/CityPatch.cs') -Raw

Assert-Equal $true ($empireSource.Contains('!CanAcceptVoluntarySubmission() || pKingdom.HasRebelledAgainst(this)')) 'Empire.canJoin must enforce both hard gates.'
Assert-Equal $true ($empireSource.Contains('if (pKingdom.HasRebelledAgainst(this)) return;')) 'Forced joins must not bypass the rebellion-origin ban.'
Assert-Equal $true ($empireSource.Contains('pKingdom.RememberRebellionOrigin(this);')) 'Rebellion leave must remember the origin before detaching.'
Assert-Equal $true ($kingdomSource.Contains('public List<long> rebellion_origin_empire_ids')) 'Rebellion origins must persist in save data.'
Assert-Equal $true ($kingdomSource.Contains('if (empire.canJoin(kingdom) && !empires.Contains(empire))')) 'Join candidates must use the unified Empire.canJoin gate.'
Assert-Equal $true ($kingdomSource.Contains('!k.CanVoluntarilyBecomeTributaryOf(empire)')) 'Direct voluntary tributary joins must enforce the de jure core gate.'
Assert-Equal $true ($kingdomSource.Contains('if (k.HasRebelledAgainst(empire)) return;')) 'Forced tribute must not bypass the original-rebellion ban.'
Assert-Equal $true ($kingdomSource.Contains('EmpireCoreManager.ContainsTitle(overlordCore, mainTitle)')) 'The tributary gate must compare the main title with the target empire core.'
Assert-Equal $true ($plotSource.Contains('!kingdom.CanVoluntarilyBecomeTributaryOf(empire)')) 'Voluntary tributary candidates must share the de jure core gate.'
Assert-Equal $true ($cityPatchSource.Contains('JoinTakenAlliance(empire, pForce: true)')) 'War-enforced tribute must retain its explicit bypass.'
Assert-Equal $true ($kingdomSource.Contains('kingdom == null || pEmpire == null || kingdom.HasRebelledAgainst(pEmpire)')) 'Low-level empire membership must reject original rebels.'
Assert-Equal $true ($kingdomSource.Contains('if (kingdom.HasRebelledAgainst(emp))')) 'Runtime membership repair must remove illegal rebel returns.'
Assert-Equal $true ($opinionSource.Contains('id = "opinion_rebel_against_empire"')) 'The original empire must receive a dedicated opinion modifier.'
Assert-Equal $true ($cityPatchSource.Contains('kingdom.RememberRebellionOrigin(rebellionOrigin);')) 'Newly created rebel polities must remember their origin.'

Write-Host 'Empire submission rules tests passed (25 assertions).'
