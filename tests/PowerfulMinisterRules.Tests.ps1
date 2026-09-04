$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Scripts/Layer/PowerfulMinisterRules.cs')
$rules = [EmpireCraft.Scripts.Layer.PowerfulMinisterRules]
$script:passed = 0
function Assert-Equal($Expected, $Actual, [string]$Name) {
    if ($Expected -ne $Actual) { throw "$Name : expected $Expected, got $Actual" }
    $script:passed++
}

Assert-Equal 300 $rules::EntryInfluence 'Entry threshold'
Assert-Equal 2 ($rules::MonthlyChange($false, $false, $true, $false, $false)) 'Ordinary minister'
Assert-Equal 4 ($rules::MonthlyChange($false, $true, $true, $false, $false)) 'Chief and dominant leader'
Assert-Equal 6 ($rules::MonthlyChange($true, $true, $true, $false, $false)) 'Regency does not stack'
Assert-Equal 6 ($rules::MonthlyChange($true, $false, $false, $false, $false)) 'Regent needs no faction leadership'
Assert-Equal -2 ($rules::MonthlyChange($false, $true, $false, $false, $false)) 'Lost central support'
Assert-Equal -2 ($rules::MonthlyChange($false, $true, $true, $false, $true)) 'Regency ending'
Assert-Equal -3 ($rules::MonthlyChange($false, $true, $true, $true, $false)) 'Strong emperor'

Assert-Equal $true ($rules::IsVulnerableNewEmperor(99, 0)) 'Low-influence accession'
Assert-Equal $false ($rules::IsVulnerableNewEmperor(100, 0)) 'Influence threshold is exclusive'
Assert-Equal $true ($rules::IsVulnerableNewEmperor(0, 35)) 'Early reign final month'
Assert-Equal $false ($rules::IsVulnerableNewEmperor(0, 36)) 'Established reign no longer accelerated'
Assert-Equal $false ($rules::IsVulnerableNewEmperor(0, -1)) 'Invalid reign age rejected'
Assert-Equal $false ($rules::IsVulnerableNewEmperor(101, 12)) 'Recovered influence ends vulnerability'
Assert-Equal 6 ($rules::MonthlyChange($false, $false, $true, $false, $false, $true)) 'Weak new emperor accelerates supported minister'
Assert-Equal 6 ($rules::MonthlyChange($false, $true, $true, $false, $false, $true)) 'Chief bonus does not stack'
Assert-Equal 6 ($rules::MonthlyChange($true, $true, $true, $false, $false, $true)) 'Regent bonus does not stack'
Assert-Equal -2 ($rules::MonthlyChange($false, $true, $false, $false, $false, $true)) 'Weak emperor does not replace central support'
Assert-Equal -2 ($rules::MonthlyChange($false, $true, $true, $false, $true, $true)) 'Regency transition retains priority'

$weakRate = $rules::MonthlyChange($false, $false, $true, $false, $false,
    $rules::IsVulnerableNewEmperor(99, 12))
$recoveredRate = $rules::MonthlyChange($false, $false, $true, $false, $false,
    $rules::IsVulnerableNewEmperor(100, 13))
Assert-Equal 6 $weakRate 'Current influence enables acceleration'
Assert-Equal 2 $recoveredRate 'Current influence recovery restores ordinary rate'

[int]$cost = 0
Assert-Equal 0 ($rules::Advance(0, 1000, 0, 3, [ref]$cost)) 'No duplicate progress in same month'
Assert-Equal 0 $cost 'No same-month cost'
Assert-Equal 0 ($rules::Advance(0, 9, 1, 3, [ref]$cost)) 'Insufficient influence pauses'
Assert-Equal 0 $cost 'Insufficient influence not consumed'
Assert-Equal 90 ($rules::Advance(0, 300, 100, 3, [ref]$cost)) 'Budget limits catch-up'
Assert-Equal 300 $cost 'Ten influence per month, not per point'
Assert-Equal 100 ($rules::Advance(99, 1000, 12, 3, [ref]$cost)) 'Cap at one hundred'
Assert-Equal 10 $cost 'Only charge needed months'
Assert-Equal 100 ($rules::Advance(100, 0, 12, 3, [ref]$cost)) 'Stable control needs no extra spending'
Assert-Equal 0 $cost 'No spending at cap'
Assert-Equal 76 ($rules::Advance(100, 0, 12, -2, [ref]$cost)) 'Control can decay without funds'
Assert-Equal 0 $cost 'Decline costs no influence'
Assert-Equal 0 ($rules::Advance(10, 100, 100, -3, [ref]$cost)) 'Decline clamps to zero'
Assert-Equal 0 ($rules::Advance(100, 0, [int]::MaxValue, -3, [ref]$cost)) 'Large elapsed time does not overflow'

foreach ($rate in 1, 2, 4, 6, 12) {
    $months = [int][Math]::Ceiling(100.0 / $rate)
    Assert-Equal 100 ($rules::Advance(0, 10000, $months, $rate, [ref]$cost)) "Reach control at rate $rate"
    Assert-Equal ($months * 10) $cost "Cost at rate $rate"
}
Assert-Equal $false ($rules::CanAdvance($true, 100, $true, $false, $false, $true, 5)) 'Cannot skip six-month interval'
Assert-Equal $true ($rules::CanAdvance($true, 100, $true, $false, $false, $true, 6)) 'Six-month interval completed'
Assert-Equal $false ($rules::CanAdvance($true, 100, $true, $false, $false, $false, 20)) 'Loyal regent cannot usurp'
Assert-Equal $false ($rules::CanAdvance($true, 100, $false, $false, $false, $true, 20)) 'Central support rechecked'
Assert-Equal $false ($rules::CanAdvance($true, 99, $true, $false, $false, $true, 20)) 'Loss of power pauses plot'
Assert-Equal $false ($rules::CanAdvance($true, 100, $true, $true, $false, $true, 20)) 'Strong emperor blocks plot'
Assert-Equal $false ($rules::CanAdvance($true, 100, $true, $false, $true, $true, 20)) 'Adulthood transition blocks plot'
Assert-Equal $false ($rules::CanAdvance($false, 100, $true, $false, $false, $true, 20)) 'Dismissed minister blocked'
Assert-Equal 80 $rules::ReleaseControlBelow 'Control release threshold'
Assert-Equal 12 $rules::RegencyRecoveryMonths 'Regency protection unchanged'
Assert-Equal $true ($rules::CanAdvance($true, 100, $true, $false, $false, $true, 0, $true)) 'Nine bestowments permits immediate usurpation plot'
Assert-Equal $false ($rules::CanAdvance($true, 100, $false, $false, $false, $true, 0, $true)) 'Nine bestowments still requires central support'
Assert-Equal $false ($rules::CanAdvance($true, 99, $true, $false, $false, $true, 0, $true)) 'Nine bestowments still requires full control'
Assert-Equal $false ($rules::CanAdvance($true, 100, $true, $false, $false, $false, 0, $true)) 'Nine bestowments does not corrupt loyal ministers'

foreach ($rate in 2, 4, 6) {
    Assert-Equal ($rate * 2) ($rules::ApplyMandate($rate, 0)) "Zero mandate doubles rate $rate"
    Assert-Equal $rate ($rules::ApplyMandate($rate, 50)) "Fifty mandate baseline $rate"
    Assert-Equal ($rate / 2) ($rules::ApplyMandate($rate, 100)) "Full mandate halves rate $rate"
    $previous = $rules::ApplyMandate($rate, 0)
    foreach ($mandate in 1..100) {
        $current = $rules::ApplyMandate($rate, $mandate)
        Assert-Equal $true ($current -le $previous -and $current -ge 1) 'Mandate never accelerates growth'
        $previous = $current
    }
}
Assert-Equal -3 ($rules::ApplyMandate(-3, 0)) 'Low mandate cannot reverse strong-emperor suppression'
Assert-Equal -2 ($rules::ApplyMandate(-2, 100)) 'High mandate does not change loss-of-support decay'
Assert-Equal 0 ($rules::ApplyMandate(0, 0)) 'Zero growth remains zero'
Assert-Equal 12 ($rules::ApplyMandate(6, -100)) 'Mandate lower bound'
Assert-Equal 3 ($rules::ApplyMandate(6, 1000)) 'Mandate upper bound'
Assert-Equal $true ($rules::ShouldRiseAgainstMinister(999, 501, $true)) 'Strong local power opposes weak minister'
Assert-Equal $false ($rules::ShouldRiseAgainstMinister(999, 500, $true)) 'Local threshold exclusive'
Assert-Equal $false ($rules::ShouldRiseAgainstMinister(1000, 501, $true)) 'Thousand influence deters opposition'
Assert-Equal $false ($rules::ShouldRiseAgainstMinister(1001, 2000, $true)) 'Above threshold deters even powerful opponents'
Assert-Equal $false ($rules::ShouldRiseAgainstMinister(0, 2000, $false)) 'Landless title alone cannot raise armies'
Assert-Equal 0 ($rules::OppositionPenalty(1000)) 'No penalty at deterrence threshold'
Assert-Equal -1 ($rules::OppositionPenalty(999)) 'Any deficit yields some resentment'
Assert-Equal 0 ($rules::OppositionPenalty(2000)) 'No positive opinion from excess influence'
Assert-Equal -20 ($rules::OppositionPenalty(900)) 'Small deficit mild penalty'
Assert-Equal -100 ($rules::OppositionPenalty(500)) 'Large deficit greater penalty'
Assert-Equal -200 ($rules::OppositionPenalty(-100)) 'Penalty capped'
Write-Output "$script:passed balance-rule assertions passed."
