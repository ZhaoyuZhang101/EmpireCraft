$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rulesPath = Join-Path $root 'Scripts/Layer/EmpireFoundingNameRules.cs'
$rules = Get-Content -Raw -Encoding UTF8 $rulesPath
Add-Type -TypeDefinition $rules

$script:passed = 0
function Assert-Choice([string]$expectedName, [string]$expectedSource, [object]$choice, [string]$message) {
    if ($choice.Name -ne $expectedName -or $choice.Source.ToString() -ne $expectedSource) {
        throw "$message. Expected $expectedName/$expectedSource, got $($choice.Name)/$($choice.Source)"
    }
    $script:passed++
}

$type = [EmpireCraft.Scripts.Layer.EmpireFoundingNameRules]
Assert-Choice '晋' 'MainTitle' ($type::Select('晋', '汉', '唐', '齐', '周', '旧国')) 'Main title must win every fallback'
Assert-Choice '汉' 'AncestralEmpire' ($type::Select('', '汉', '唐', '齐', '周', '旧国')) 'Ancestral imperial name is the first fallback'
Assert-Choice '唐' 'AncestralTitle' ($type::Select('', '', '唐', '齐', '周', '旧国')) 'Ancestral title fallback failed'
Assert-Choice '齐' 'OwnedTitle' ($type::Select('', '', '', '齐', '周', '旧国')) 'Owned primary title fallback failed'
Assert-Choice '周' 'EmpireCore' ($type::Select('', '', '', '', '周', '旧国')) 'Empire core fallback failed'
Assert-Choice '旧国' 'KingdomName' ($type::Select('', '', '', '', '', '旧国')) 'Kingdom name fallback failed'

$empire = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/Layer/Empire.cs')
$createStart = $empire.IndexOf('public void CreateNewEmpire(')
$createEnd = $empire.IndexOf('public bool CanSetTitleToPreviousEmperor()', $createStart)
if ($createStart -lt 0 -or $createEnd -le $createStart) { throw 'CreateNewEmpire source not found' }
$create = $empire.Substring($createStart, $createEnd - $createStart)
if ($create.Contains('kingdom.king.GetTitle()')) { throw 'Founding still selects the actor title list directly' }
if (-not $create.Contains('SelectFoundingEmpireName(kingdom, riseCore)')) { throw 'Founding name rules are not wired' }
if (-not $create.Contains('RecordFoundingNameHistory(foundingName, kingdom.king)')) { throw 'Founding name reason is not recorded' }
if (-not $create.Contains('actorId: kingdom.king.id') -or -not $create.Contains('kingdomId: kingdom.id')) {
    throw 'Founding history is not bound to its ruler and kingdom'
}
$script:passed += 4
$newEmperorIndex = $create.IndexOf('NewEmperor(kingdom.king, !isSplit)')
$nameReasonIndex = $create.IndexOf('RecordFoundingNameHistory(foundingName, kingdom.king)')
$foundationIndex = $create.IndexOf('this.RecordHistory(EmpireHistoryType.new_empire_history')
if ($newEmperorIndex -lt 0 -or $nameReasonIndex -le $newEmperorIndex -or $foundationIndex -le $nameReasonIndex) {
    throw 'Founding records are not written after reign initialization in prepend order'
}
$script:passed++

$history = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Scripts/GeneralSystems/HistoryRecordSystem.cs')
if (-not $history.Contains('bool prepend = false') -or
    ([regex]::Matches($history, 'if \(prepend\) empire\.data\.currentHistory\.descriptions\.Insert\(0, description\);').Count -ne 2)) {
    throw 'Unified history writer does not support ordered prepending for both record paths'
}
$script:passed++

Write-Output "$script:passed empire founding-name assertions passed."
