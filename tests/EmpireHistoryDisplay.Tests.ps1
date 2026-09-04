$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Scripts/Layer/EmpireHistoryDisplay.cs')
$display = [EmpireCraft.Scripts.Layer.EmpireHistoryDisplay]
$script:passed = 0
function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) { throw "$message : expected $expected, got $actual" }
    $script:passed++
}
$hair = [char]0x200A
Assert-Equal 'Northern Song Dynasty' ($display::FullName('Northern Song Dynasty', 'Song', 'Southern Song Dynasty', 'Song', '?')) 'Historical snapshot wins over later direction changes'
Assert-Equal 'WestHanEmpire' ($display::FullName($null, 'Han', "West${hair}Han${hair}Empire", 'Han', '?')) 'Old save retains direction and suffix from matching empire'
Assert-Equal 'Song' ($display::FullName($null, 'Song', 'Han Empire', 'Han', '?')) 'Different dynasty cannot overwrite history'
Assert-Equal 'Song' ($display::FullName($null, 'Song', $null, $null, '?')) 'Lost historical prefixes are not invented'
Assert-Equal '?' ($display::FullName($null, $null, $null, $null, '?')) 'Missing name explicit fallback'
Assert-Equal 'Northern Song Dynasty' ($display::FullName('Northern Song Dynasty', $null, $null, $null, '?')) 'Snapshot survives loss of political empire'
Assert-Equal 'Han Empire' ($display::FullName('', 'Han', 'Han Empire', 'Han', '?')) 'Empty snapshot is backward compatible'
Assert-Equal 'Northern Song Dynasty' ($display::FullName($null, 'Song', 'Northern Song Dynasty', 'Song', '?')) 'Word spacing preserved'
Write-Output "$script:passed history display assertions passed."

$ui = Get-Content (Join-Path $root 'Scripts/UI/Windows/EmpireCoreWindow.cs') -Raw
$records = Get-Content (Join-Path $root 'Scripts/GeneralSystems/HistoryRecordSystem.cs') -Raw
$empire = Get-Content (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$data = Get-Content (Join-Path $root 'Scripts/Layer/EmpireData.cs') -Raw
$checks = @{
    'Fixed four-column grid' = $ui.Contains('BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount')
    'Grid cells fit content width' = (4 * 48 + 3 * 2 -le 200) -and $ui.Contains('pCellSize: new Vector2(48, 34)')
    'Title cards attach to grid' = $ui.Contains('AddTitleCard(AutoGridLayoutGroup parent') -and $ui.Contains('AddTitleCard(titleGrid, title)')
    'All history constructors preserve full name' = ([regex]::Matches($records, 'new EmpireCraftHistory').Count -eq [regex]::Matches($records, 'empire_full_name = empire.GetEmpireFullName\(\)').Count)
    'Empire constructors preserve full name' = ([regex]::Matches($empire, 'new EmpireCraftHistory').Count -eq [regex]::Matches($empire, 'empire_full_name = this.GetEmpireFullName\(\)').Count)
    'Full name is serialized independently of short name' = $data.Contains('public string empire_full_name { get; set; }') -and $data.Contains('public string empire_name { get; set; }')
    'Royal surname included in cards' = $ui.Contains('histories.Select(h => h.royal_surname)')
    'Current reign included' = $ui.Contains('result.Add(activeEmpire.data.currentHistory)')
    'Not ordered by longest reign' = -not $ui.Contains('OrderByDescending(h => h?.total_time')
    'Long names use hover clipping' = $ui.Contains('HoverMarqueeText.Attach(historyDetails.AddTextIntoVertLayout(empireName')
}
foreach ($check in $checks.GetEnumerator()) { if (-not $check.Value) { throw $check.Key } }
Write-Output "$($checks.Count) UI and history wiring checks passed."
