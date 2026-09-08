#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$checks = [ordered]@{}

$regimes = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/Regimes/RegimeSystem.cs')
$kingdoms = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs')
$targets = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/Regimes/TemporaryFactions/TemporaryFaction.cs')
$religionClaim = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_宗教同化.cs')
$translate = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/HelperFunc/TranslateHelper.cs')
$clans = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/GameClassExtensions/ClanExtension.cs')
$avatars = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/UI/Components/UIHelper.cs')
$bureau = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/UI/Windows/EmpireBeaurauWindow.cs')
$nameplates = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/GameLibrary/EmpireCraftNamePlateLibrary.cs')
$cabinet = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/AI/EmpireAI/EmpireCraftEmpireBehCheckCabinet.cs')

$checks['Regime clone rejects disposed kingdoms'] = $regimes.Contains('if (kingdom?.data == null || kingdom.isRekt()) return null;')
$checks['Regime lookup rejects disposed kingdoms'] = $kingdoms.Contains('if (k?.data == null || k.isRekt()) return null;')
$checks['Claim target rejects disposed kingdoms'] = $targets.Contains('k?.data == null || k.isRekt() ? -1L : k.getID()')
$checks['Religion claim skips disposed members'] = $religionClaim.Contains('if (kingdom?.data == null || kingdom.isRekt()) continue;')
$checks['Western accession has a capital fallback'] = $translate.Contains('emperor.kingdom.capital?.GetCityName() ?? ""')
$checks['Clan formatter rejects disposed clans'] = $clans.Contains('if (clan?.data == null || string.IsNullOrWhiteSpace(clan.name)) return "";')
$checks['Avatar loader rejects disposed actors'] = $avatars.Contains('if (actor?.data == null || actor.isRekt()) actor = null;')
$checks['Bureau handles an empty cabinet list'] = $bureau.Contains('_empire.GetCabinetMembers() ?? new List<Actor>()')
$checks['Nameplates rebuild invalid banners'] = $nameplates.Contains('catch (ArgumentOutOfRangeException)') -and $nameplates.Contains('kingdom.generateBanner();')
$checks['Feudal cabinet filters disposed kingdoms'] = $cabinet.Contains('.Where(k => k?.data != null && !k.isRekt())')

foreach ($check in $checks.GetEnumerator()) {
    if (-not $check.Value) { throw $check.Key }
}
Write-Output "$($checks.Count) September 8 bug-report regression assertions passed."
