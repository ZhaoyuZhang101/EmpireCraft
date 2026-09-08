$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$languages = @('cz', 'ch', 'en')
$localeMaps = @{}
$allowedBlanks = @('empty', 'SpecificClanWindowTitle', 'EmpireWindowTitle', 'EmpireCoreWindowTitle')
$allowedCaseGroups = @(
    'culture', 'empire_layer', 'empire_layer_description', 'king', 'language',
    'ui_zone_mode_empire_0', 'ui_zone_mode_empire_1', 'ui_zone_mode_empire_2'
)
$passed = 0

foreach ($language in $languages) {
    $path = Join-Path $root "Locales/$language.json"
    $raw = Get-Content -Raw -Encoding UTF8 $path
    $localeMaps[$language] = $raw | ConvertFrom-Json -AsHashtable
    $declaredKeys = [regex]::Matches($raw, '(?m)^\s*"((?:\\.|[^"])*)"\s*:') |
        ForEach-Object { $_.Groups[1].Value }
    $exactDuplicates = @($declaredKeys | Group-Object -CaseSensitive | Where-Object Count -gt 1)
    if ($exactDuplicates.Count -gt 0) { throw "$language has duplicate JSON keys: $($exactDuplicates.Name -join ', ')" }

    $caseGroups = @($declaredKeys | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1 |
        ForEach-Object Name | Sort-Object)
    $unexpectedCaseGroups = @($caseGroups | Where-Object { $_ -notin $allowedCaseGroups })
    if ($unexpectedCaseGroups.Count -gt 0) {
        throw "$language has unexpected case-only key collisions: $($unexpectedCaseGroups -join ', ')"
    }

    $blankKeys = @($localeMaps[$language].GetEnumerator() |
        Where-Object { [string]::IsNullOrWhiteSpace([string]$_.Value) } | ForEach-Object Key | Sort-Object)
    if (@($blankKeys | Where-Object { $_ -notin $allowedBlanks }).Count -gt 0) {
        throw "$language has untranslated blank values: $($blankKeys -join ', ')"
    }
    $passed += 3
}

$baseline = @($localeMaps['cz'].Keys | Sort-Object)
foreach ($language in @('ch', 'en')) {
    $difference = Compare-Object $baseline @($localeMaps[$language].Keys | Sort-Object)
    if ($difference) { throw "$language JSON key set differs from cz" }
    $passed++
}

$csvFiles = @(Get-ChildItem (Join-Path $root 'Locales') -Filter *.csv -File) +
    @(Get-ChildItem (Join-Path $root 'Scripts/Regimes/Configs') -Recurse -Filter OfficialType.csv -File)
$csvKeys = @()
foreach ($file in $csvFiles) {
    $rows = @(Import-Csv -Encoding UTF8 $file.FullName)
    if ($rows.Count -eq 0) { throw "Empty localization CSV: $($file.FullName)" }
    $headers = @($rows[0].PSObject.Properties.Name)
    foreach ($column in @('key', 'cz', 'en', 'ch')) {
        if ($column -notin $headers) { throw "$($file.Name) is missing column $column" }
    }
    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.key)) { throw "$($file.Name) contains a blank key" }
        foreach ($language in $languages) {
            if ([string]::IsNullOrWhiteSpace([string]$row.$language)) {
                throw "$($file.Name) has no $language translation for $($row.key)"
            }
        }
        $csvKeys += $row.key
    }
    $passed++
}
$csvDuplicates = @($csvKeys | Group-Object | Where-Object Count -gt 1)
if ($csvDuplicates.Count -gt 0) { throw "Duplicate CSV localization keys: $($csvDuplicates.Name -join ', ')" }
if (@($csvKeys | Where-Object { $localeMaps['en'].ContainsKey($_) }).Count -gt 0) {
    throw 'Localization keys overlap between JSON and CSV sources'
}
$passed += 2

$allKeys = [System.Collections.Generic.HashSet[string]]::new()
foreach ($key in $localeMaps['en'].Keys) { [void]$allKeys.Add($key) }
foreach ($key in $csvKeys) { [void]$allKeys.Add($key) }
foreach ($config in Get-ChildItem (Join-Path $root 'Scripts/Regimes/Configs') -Recurse -Filter SystemConfig.json -File) {
    $raw = Get-Content -Raw -Encoding UTF8 $config.FullName
    $regime = [regex]::Match($raw, '(?m)^\s*"([^"]+)"\s*:\s*\{').Groups[1].Value
    $expected = @([regex]::Matches($raw, '"pre"\s*:\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $expected += @([regex]::Matches($raw, '"type"\s*:\s*(\d+)') |
        ForEach-Object { "${regime}_officiallevel_$($_.Groups[1].Value)" })
    $missing = @($expected | Sort-Object -Unique | Where-Object { -not $allKeys.Contains($_) })
    if ($missing.Count -gt 0) { throw "$($config.Directory.Name) has missing office keys: $($missing -join ', ')" }
    $passed++
}

$sourceText = (Get-ChildItem (Join-Path $root 'Scripts') -Recurse -Filter *.cs -File |
    ForEach-Object { Get-Content -Raw -Encoding UTF8 $_.FullName }) -join "`n"
$directKeys = @([regex]::Matches($sourceText, 'LM\.(?:Get|GetNoColor|ColorText)\(\s*"([^"]+)"') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$missingDirectKeys = @($directKeys | Where-Object { -not $allKeys.Contains($_) -and $_ -notin @('class_', 'default_') })
if ($missingDirectKeys.Count -gt 0) {
    throw "Static localization calls have missing keys: $($missingDirectKeys -join ', ')"
}
$passed++

$enumSpecs = @(
    @('KingdomType', 'Scripts/Regimes/RegimeSystem.cs'),
    @('CityType', 'Scripts/Regimes/RegimeSystem.cs'),
    @('TemporaryFactionType', 'Scripts/Regimes/TemporaryFactions/TemporaryFactionType.cs'),
    @('FactionType', 'Scripts/Regimes/FixedFaction.cs')
)
foreach ($spec in $enumSpecs) {
    $code = Get-Content -Raw -Encoding UTF8 (Join-Path $root $spec[1])
    $match = [regex]::Match($code, "(?s)public enum $($spec[0])\s*\{(.*?)\}")
    if (-not $match.Success) { throw "Enum source not found: $($spec[0])" }
    $body = [regex]::Replace($match.Groups[1].Value, '(?m)//.*$', '')
    $body = [regex]::Replace($body, '(?s)\[[^\]]+\]\s*', '')
    $values = @($body -split ',' | ForEach-Object { ($_ -replace '=.*$', '').Trim() } | Where-Object { $_ })
    $missingValues = @($values | Where-Object { -not $allKeys.Contains($_) })
    if ($missingValues.Count -gt 0) {
        throw "$($spec[0]) has missing localization keys: $($missingValues -join ', ')"
    }
    $passed++
}

foreach ($key in $baseline) {
    $tokens = @([regex]::Matches([string]$localeMaps['cz'][$key], '\$\w+\$|\{\d+\}') |
        ForEach-Object Value | Sort-Object -Unique)
    foreach ($language in @('ch', 'en')) {
        $other = @([regex]::Matches([string]$localeMaps[$language][$key], '\$\w+\$|\{\d+\}') |
            ForEach-Object Value | Sort-Object -Unique)
        if (Compare-Object $tokens $other) { throw "$key has mismatched placeholders in $language" }
    }
}
$passed++

Write-Output "$passed localization coverage assertions passed across $($localeMaps['en'].Count) JSON keys and $($csvKeys.Count) CSV keys."
