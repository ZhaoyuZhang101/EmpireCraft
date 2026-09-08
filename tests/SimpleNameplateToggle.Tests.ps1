$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script:assertions = 0

function Assert-True([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:assertions++
}

$modClass = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/ModClass.cs')
$button = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/GodPowers/SwitchSimpleNameplateButton.cs')
$mainTab = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/UI/MainTab.cs')
$nameplates = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/GameLibrary/EmpireCraftNamePlateLibrary.cs')
$saveData = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/Data/SaveData.cs')
$dataManager = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/Data/DataManager.cs')

Assert-True ($modClass.Contains('public static bool SIMPLE_NAMEPLATE_SWITCH = false;')) 'Simple-nameplate state is missing'
Assert-True ($button.Contains('id = "simple_nameplate"')) 'GodPower id is not registered'
Assert-True ($button.Contains('toggle_name = "switch_simple_nameplate"')) 'GodPower toggle state is not registered'
Assert-True ($mainTab.Contains('SwitchSimpleNameplateButton.init();')) 'GodPower button is not added to the Empire tab'
Assert-True ($mainTab.Contains('CreateToggleButton("simple_nameplate"')) 'Simple-nameplate control is not a toggle button'
Assert-True ($nameplates.Contains('GetSafeKingdomName(pMetaObject)')) 'Legacy kingdom labels bypass the simple-name selector'
Assert-True ($nameplates.Contains('GetSafeEmpireName(empire)')) 'Empire labels bypass the simple-name selector'
Assert-True ($nameplates.Contains('result.Replace(ModClass.NARROW_SPACE, "")')) 'Directional name parts are not joined after suffix removal'
Assert-True ($saveData.Contains('public bool switch_simple_nameplate = false;')) 'Toggle is not represented in save data'
Assert-True ($dataManager.Contains('saveData.switch_simple_nameplate = ModClass.SIMPLE_NAMEPLATE_SWITCH;')) 'Toggle is not saved'
Assert-True ($dataManager.Contains('ModClass.SIMPLE_NAMEPLATE_SWITCH = saveData.switch_simple_nameplate;')) 'Toggle is not restored'

foreach ($language in @('cz', 'ch', 'en')) {
    $locale = Get-Content -Raw -Encoding UTF8 (Join-Path $root "Locales/$language.json")
    Assert-True ($locale -match '"simple_nameplate"\s*:') "$language is missing the toggle name"
    Assert-True ($locale -match '"simple_nameplate_description"\s*:') "$language is missing the toggle description"
}

Write-Output "$script:assertions simple-nameplate toggle assertions passed."
