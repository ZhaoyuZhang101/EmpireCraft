$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Add-Type -Path (Join-Path $root 'Scripts/Layer/NationalPowerRules.cs')

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) {
        throw "$message Expected '$expected', got '$actual'."
    }
}

[type]$rules = [EmpireCraft.Scripts.Layer.NationalPowerRules]

Assert-Equal 60d ($rules::Calculate(100, 0, 0)) 'Population must contribute 60 percent.'
Assert-Equal 30d ($rules::Calculate(0, 100, 0)) 'Military must contribute 30 percent.'
Assert-Equal 10d ($rules::Calculate(0, 0, 100)) 'Economy must contribute 10 percent.'
Assert-Equal 0d ($rules::Calculate(-100, -100, -100)) 'Negative inputs must be clamped to zero.'
Assert-Equal $true ($rules::IsWithinShare(200, 1000, $rules::TributaryMaximumShare)) 'Exactly one fifth must be tributary eligible.'
Assert-Equal $false ($rules::IsWithinShare(201, 1000, $rules::TributaryMaximumShare)) 'More than one fifth must be rejected.'
Assert-Equal $true ($rules::IsWithinShare(100, 1000, $rules::SubmissionMaximumShare)) 'Exactly one tenth must be submission eligible.'
Assert-Equal $false ($rules::IsWithinShare(101, 1000, $rules::SubmissionMaximumShare)) 'More than one tenth must be rejected.'
Assert-Equal $false ($rules::IsWithinShare(0, 0, $rules::SubmissionMaximumShare)) 'An empty empire cannot accept by ratio.'

$kingdomSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$empireSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$empireDataSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/EmpireData.cs') -Raw
$plotSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw
$kingdomWindowSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/KingdomWindowPatch.cs') -Raw
$empireWindowSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/EmpireWindow.cs') -Raw
$cityPatchSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/CityPatch.cs') -Raw
$empireManagerSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/EmpireManager.cs') -Raw
$createEmpireSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/GodPowers/CreateEmpireButton.cs') -Raw

Assert-Equal $true ($kingdomSource.Contains('public static void RefreshNationalPower(this Kingdom kingdom, bool force = false)')) 'Kingdoms must own an annual power cache.'
Assert-Equal $true ($kingdomSource.Contains('Date.getYearsSince(data.last_national_power_timestamp) < 1')) 'Kingdom power must refresh annually.'
Assert-Equal $true ($empireSource.Contains('public void RefreshNationalPower(bool force = false)')) 'Empires must own an annual power cache.'
Assert-Equal $true ($empireDataSource.Contains('annual_power_index')) 'Empire power must persist in empire data.'
Assert-Equal $true ($empireSource.Contains('ContainsKingdomCapitalInCore(pKingdom)')) 'Voluntary submission must require the target core area.'
Assert-Equal $true ($empireSource.Contains('MeetsSubmissionPowerThreshold(pKingdom)')) 'Voluntary submission must use the one-tenth threshold.'
Assert-Equal $true ($kingdomSource.Contains('empire.MeetsTributaryPowerThreshold(k)')) 'Direct voluntary tribute must use the one-fifth threshold.'
Assert-Equal $true ($plotSource.Contains('empire.MeetsTributaryPowerThreshold(kingdom)')) 'Tributary plot selection must use the one-fifth threshold.'
Assert-Equal $false ($plotSource.Contains('int kingdomWarriors = kingdom.countTotalWarriors();')) 'Raw military-only comparison must be removed.'
Assert-Equal $false ($plotSource.Substring($plotSource.IndexOf('id = "kingdom_join_empire"'), 900).Contains('if (kingdom.HasMainTitle()) return false;')) 'A title must not independently block same-core submission.'
Assert-Equal $true ($cityPatchSource.Contains('JoinTakenAlliance(empire, pForce: true)')) 'War-enforced tribute must keep its force bypass.'
Assert-Equal $true ($empireManagerSource.Contains('empire.join(pKingdom2, pForce: true)')) 'Forced empire creation must bypass voluntary thresholds.'
Assert-Equal $true ($createEmpireSource.Contains('empire.join(JoinKingdom, pForce: true)')) 'God-power empire creation must bypass voluntary thresholds.'
Assert-Equal $true ($plotSource.Contains('empire.join(kingdom1, pForce: true)')) 'Alliance-wide empire formation must bypass voluntary thresholds.'
Assert-Equal $true ($kingdomWindowSource.Contains('showStatRow("national_power"')) 'Kingdom window must display national power.'
Assert-Equal $true ($empireWindowSource.Contains('LM.Get("national_power")')) 'Empire window must display national power.'

Write-Host 'National power rules tests passed (25 assertions).'
