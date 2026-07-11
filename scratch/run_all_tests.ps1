# GymTrackPro Master E2E Integration Test Runner

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Master E2E Test Runner         " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$scripts = @(
    "scratch/auth_integration_test.ps1",
    "scratch/members_integration_test.ps1",
    "scratch/attendance_integration_test.ps1",
    "scratch/plans_integration_test.ps1",
    "scratch/payments_integration_test.ps1",
    "scratch/ops_analytics_integration_test.ps1",
    "scratch/settings_integration_test.ps1",
    "scratch/notifications_integration_test.ps1"
)

$hasFailures = $false
$totalTests = 0
$passedTests = 0

foreach ($script in $scripts) {
    Write-Host "Running: $script..." -ForegroundColor Yellow
    
    # Hard-kill any lingering dotnet/API processes before each suite
    Get-Process -Name "GymTrackPro.API" -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3   # Give OS time to release port 5221 and file locks

    $output = powershell -ExecutionPolicy Bypass -File $script

    # Each test script prints a "===...===" summary divider near the end, 
    # then lists each result exactly once. Count only those summary lines.
    $inSummary = $false
    $passes    = 0
    $failures  = 0
    $failLines = @()

    foreach ($line in $output) {
        # The summary block starts after "Results Summary" or the final "===" banner
        if ($line -match "Results Summary|E2E Summary|Test Results") {
            $inSummary = $true
            continue
        }
        if ($inSummary) {
            if ($line -match "^\[PASS\]") { $passes++ }
            elseif ($line -match "^\[FAIL\]") {
                $failures++
                $failLines += $line
            }
        }
    }

    $totalTests  += ($passes + $failures)
    $passedTests += $passes

    if ($failures -gt 0) {
        $hasFailures = $true
        Write-Host " -> $script had $failures failures!" -ForegroundColor Red
        $failLines | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host " -> $script passed successfully ($passes tests)." -ForegroundColor Green
    }
    Write-Host "---------------------------------------------"
}

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Master E2E Verification Complete           " -ForegroundColor Cyan
Write-Host "  Total Tests Run: $totalTests"               -ForegroundColor Cyan
Write-Host "  Passed: $passedTests"                       -ForegroundColor Green
if ($hasFailures) {
    Write-Host "  Failed: $($totalTests - $passedTests)"  -ForegroundColor Red
    exit 1
} else {
    Write-Host "  All tests passed successfully!"         -ForegroundColor Green
    exit 0
}
