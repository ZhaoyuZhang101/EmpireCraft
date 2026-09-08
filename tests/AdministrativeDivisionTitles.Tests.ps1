$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$kingdom = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$kingdomType = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckKingdomType.cs') -Raw
$empire = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$claim = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_设置行政区.cs') -Raw

Add-Type -Path (Join-Path $root 'Scripts/Layer/AdministrativeDivisionNameRules.cs')
[type]$nameRules = [EmpireCraft.Scripts.Layer.AdministrativeDivisionNameRules]
$administrationIds = [long[]](12, 8, 20, 8)
if ($nameRules::SelectProvinceNameHolder(12, $administrationIds) -ne 12) {
    throw 'The administration controlling the de jure capital must receive the province name'
}
if ($nameRules::SelectProvinceNameHolder(99, $administrationIds) -ne 8) {
    throw 'Without a capital controller, province-name assignment must be deterministic'
}
if ($nameRules::SelectProvinceNameHolder(99, [long[]]@()) -ne -1) {
    throw 'An empty title must not assign its province name'
}

if (!$kingdom.Contains('public long AdministrativeTitle = -1L;')) {
    throw 'Administrative title authorization is not persisted separately'
}
if (!$kingdom.Contains('public static KingdomTitle GetAdministrativeTitle(this Kingdom kingdom)')) {
    throw 'Administrative title lookup is missing'
}
if (!$kingdom.Contains('kingdom.GetMainTitle() == null;')) {
    throw 'A formally owned title can still be treated as delegated administration'
}
if (!$kingdom.Contains('title?.data?.province_name')) {
    throw 'Administrative title does not expose its province name'
}
if (!$kingdom.Contains('if (!kingdom.CanUseAdministrativeProvinceName(title)) return null;')) {
    throw 'Every delegated administration can still reuse the same province name'
}
if (!$kingdom.Contains('title.RefreshAdministrativeDivisionNames();')) {
    throw 'Administrative assignment does not refresh all competing names'
}
if (!$kingdomType.Contains('string administrativeName = pKingdom.GetAdministrativeProvinceName();')) {
    throw 'Kingdom naming does not prioritize the delegated province name'
}
if (!$claim.Contains('k.SetAdministrativeTitle(title);') -or $claim.Contains('k.king?.AddOwnedTitle(title)')) {
    throw 'Administrative-division claim grants ownership or omits authorization'
}
if (!$empire.Contains('AutoEstablishLvLingAdministrativeDivisions();')) {
    throw 'Automatic LvLing division does not use de jure ranges'
}
$autoStart = $empire.IndexOf('public void AutoEnfeoff()')
$lvLingBranch = $empire.IndexOf('if (regime?.type == RegimeType.LvLing)', $autoStart)
$virtualBranch = $empire.IndexOf('if (regime != null && regime.enfeoff_virtual_only)', $autoStart)
if ($lvLingBranch -lt 0 -or $virtualBranch -lt 0 -or $lvLingBranch -gt $virtualBranch) {
    throw 'Virtual peerage mode still prevents LvLing administrative division'
}
if (!$empire.Contains('List<City> region = title.city_list')) {
    throw 'Automatic LvLing division is not built from title territory'
}
if (!$empire.Contains('newKingdom.SetAdministrativeTitle(title);')) {
    throw 'Automatic LvLing division does not retain its delegated range'
}
if (!$empire.Contains('bool delegatedOnly = savedKingdom?.GetAdministrativeTitle() == title')) {
    throw 'Delegated administration can still be mistaken for landed ownership'
}

Write-Output '16 administrative division title assertions passed.'
