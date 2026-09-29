$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'bin/Debug/net48/EmpireCraft.dll'))
$type = $assembly.GetType('EmpireCraft.Scripts.AI.EmpireCraftPlotsAddition', $true)
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$usesVanilla = $type.GetMethod('UsesVanillaWarPlot', $flags)
$canInitiate = $type.GetMethod('CanInitiateNormalWar', $flags)
if ($null -eq $usesVanilla -or $null -eq $canInitiate) {
    throw 'Normal-war routing methods were not found.'
}

$cases = @(
    @{ Name = 'independent'; Empire = $false; Member = $false; Overlord = $false; Vanilla = $true; Initiates = $true },
    @{ Name = 'feudal subject'; Empire = $false; Member = $false; Overlord = $true; Vanilla = $false; Initiates = $true },
    @{ Name = 'imperial core'; Empire = $true; Member = $true; Overlord = $false; Vanilla = $false; Initiates = $true },
    @{ Name = 'imperial member'; Empire = $false; Member = $true; Overlord = $false; Vanilla = $false; Initiates = $false },
    @{ Name = 'imperial vassal'; Empire = $false; Member = $true; Overlord = $true; Vanilla = $false; Initiates = $false }
)
foreach ($case in $cases) {
    $vanilla = $usesVanilla.Invoke($null, @($case.Empire, $case.Member, $case.Overlord))
    $initiates = $canInitiate.Invoke($null, @($case.Empire, $case.Member))
    if ($vanilla -ne $case.Vanilla -or $initiates -ne $case.Initiates) {
        throw "Unexpected normal-war routing for $($case.Name)."
    }
}
Write-Output 'Five normal-war routing scenarios passed.'
