#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$service = Get-Content -LiteralPath (Join-Path $root 'Scripts/Diagnostics/BugReportService.cs') -Raw
$window = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/BugReportWindow.cs') -Raw
$tab = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/MainTab.cs') -Raw

$checks = [ordered]@{
    'Recipient is the requested address' = $service.Contains('public const string Recipient = "zhangzhaoyu101@gmail.com";')
    'Player log has Unity and platform fallbacks' = $service.Contains('consoleLogPath') -and $service.Contains('"LocalLow"') -and $service.Contains('"Library"') -and $service.Contains('".config"')
    'Live Player.log is copied safely' = $service.Contains('FileShare.ReadWrite | FileShare.Delete')
    'Player.log is added to the upload' = $service.Contains('Name = "Player.log"') -and $service.Contains('Data = playerLogBytes')
    'Diagnostic summary is added to the upload' = $service.Contains('Name = "EmpireCraft-report.txt"') -and $service.Contains('BuildSummary()')
    'No SMTP credential is embedded' = -not $service.Contains('SmtpClient') -and -not $service.Contains('password')
    'Reports are uploaded as multipart form data' = $service.Contains('multipart/form-data; boundary=') -and $service.Contains('BuildMultipartBody(files, boundary)')
    'Upload retries through a fallback endpoint' = $service.Contains('UploadWithFallback(files);') -and $service.Contains('foreach (string endpoint in BugReportEndpoints)')
    'Upload size is bounded' = $service.Contains('MaximumCombinedUploadBytes') -and $service.Contains('totalBytes > MaximumCombinedUploadBytes')
    'Bug report window follows window structure' = $window.Contains('class BugReportWindow : AutoLayoutWindow<BugReportWindow>')
    'Window discloses log privacy before sending' = $window.Contains('bug_report_privacy_notice')
    'Window is registered centrally' = $tab.Contains('BugReportWindow.CreateWindow(nameof(BugReportWindow)')
    'Button is added to the existing Empire group' = $tab.Contains('tab.AddPowerButton(EMPIRE_GROUP, bugReportButton);')
    'Original debug icon is used' = $tab.Contains('SpriteTextureLoader.getSprite("ui/icons/iconDebug")')
}
foreach ($check in $checks.GetEnumerator()) {
    if (-not $check.Value) { throw $check.Key }
}

foreach ($localeName in 'cz.json', 'ch.json', 'en.json') {
    $localePath = Join-Path $root "Locales/$localeName"
    $locale = Get-Content -LiteralPath $localePath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in 'bug_report_window_title', 'bug_report_button', 'bug_report_privacy_notice',
             'bug_report_send', 'bug_report_open_log', 'bug_report_sent', 'bug_report_failed') {
        if (-not $locale.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($locale[$key])) {
            throw "$localeName is missing $key"
        }
    }
}

Write-Output "$($checks.Count) bug-report structure and delivery assertions passed."
Write-Output '3 locale files parsed and contain the required bug-report strings.'
