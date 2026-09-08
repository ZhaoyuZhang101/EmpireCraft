$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$claimNames = @(
    '颁布金玺诏书',
    '恢复世袭皇权',
    '颁布帝国治安令',
    '确认诸侯特权',
    '争夺主教叙任权'
)
$enumSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/TemporaryFactionType.cs') -Raw
$factionSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/FixedFaction.cs') -Raw

foreach ($claimName in $claimNames) {
    if (!$enumSource.Contains($claimName)) { throw "Missing claim enum: $claimName" }
    $claimPath = Join-Path $root "Scripts/Regimes/TemporaryFactions/Claims/TempFac_$claimName.cs"
    if (!(Test-Path -LiteralPath $claimPath)) { throw "Missing claim implementation: $claimName" }
    $claimSource = Get-Content -LiteralPath $claimPath -Raw
    if (!$claimSource.Contains('HolyRomanClaimRules.IsEligible') -and
        !$claimSource.Contains('HolyRomanClaimRules.HasElectoralCollege') -and
        !$claimSource.Contains('HolyRomanClaimRules.GetPrinces')) {
        throw "Claim is not restricted to Western feudal empires: $claimName"
    }
}

foreach ($mapping in @(
    'FactionType.中央',
    'FactionType.自治',
    'FactionType.神权',
    'MigrateFeudalClaimCatalog',
    'ClaimCatalogVersion'
)) {
    if (!$factionSource.Contains($mapping)) { throw "Missing faction wiring: $mapping" }
}

$goldenBull = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_颁布金玺诏书.cs') -Raw
$hereditary = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_恢复世袭皇权.cs') -Raw
$peace = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_颁布帝国治安令.cs') -Raw
$privilege = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_确认诸侯特权.cs') -Raw
$investiture = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_争夺主教叙任权.cs') -Raw
$selector = Get-Content -LiteralPath (Join-Path $root 'Scripts/HelperFunc/OfficeSelector.cs') -Raw
$executePatch = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/TemporaryFactionExecutePatch.cs') -Raw
$worldLogs = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameLibrary/EmpireCraftWorldLogLibrary.cs') -Raw
$historyHelper = Get-Content -LiteralPath (Join-Path $root 'Scripts/HelperFunc/TranslateHelper.cs') -Raw

if (!$goldenBull.Contains('SetLeaderSelectMethod(LeaderSelectMethod.Vote)')) { throw 'Golden Bull does not enable elective succession' }
if (!$hereditary.Contains('SetLeaderSelectMethod(LeaderSelectMethod.Succession)')) { throw 'Hereditary claim does not restore succession' }
if (!$peace.Contains('SetAllowDiplomacy(false)') -or !$peace.Contains('SetAllowSupportCenterArmy(true)')) { throw 'Imperial Peace does not centralize war rights' }
if (!$privilege.Contains('SetAllowDiplomacy(true)') -or !$privilege.Contains('SetAllowSupportCenterArmy(false)')) { throw 'Princely privilege does not restore local rights' }
if (!$investiture.Contains('FactionType.中央 => ReligionLevel.Medium') -or
    !$investiture.Contains('FactionType.神权 => ReligionLevel.High')) { throw 'Investiture claim is not faction-sensitive' }
if (!$selector.Contains('TryGetFeudalElectiveRuler(office, pKingdom)') -or
    !$selector.Contains('empire.GetCabinetMembers()')) { throw 'Elective succession is not connected to the elector college' }
if (!$executePatch.Contains('__instance.CompletionOutcome')) { throw 'Detailed claim result is not forwarded by the execution patch' }
if (!$worldLogs.Contains('temporary_faction_success_outcome_log')) { throw 'Detailed claim result world log is not registered' }
if (!$historyHelper.Contains('RecordNationalHistoryIntoEmpire(empire, empire.Emperor)')) { throw 'Detailed claim result is not recorded in imperial history' }

foreach ($claimSource in $goldenBull, $hereditary, $peace, $privilege, $investiture) {
    if (!$claimSource.Contains('CompletionOutcome =')) { throw 'A Holy Roman claim has no detailed completion result' }
}

foreach ($locale in 'ch.json', 'cz.json', 'en.json') {
    $localePath = Join-Path $root "Locales/$locale"
    $translations = Get-Content -LiteralPath $localePath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in $claimNames + @('temporary_faction_success_outcome_log', 'holy_roman_outcome_golden_bull',
             'holy_roman_outcome_hereditary', 'holy_roman_outcome_imperial_peace',
             'holy_roman_outcome_princely_privileges', 'holy_roman_outcome_investiture_crown',
             'holy_roman_outcome_investiture_clergy')) {
        if (!$translations.ContainsKey($key)) { throw "$locale is missing $key" }
    }
}

Write-Output 'Holy Roman claim behavior, outcome broadcast, history, migration, election, and locale checks passed.'
