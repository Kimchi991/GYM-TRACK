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
    $output = powershell -ExecutionPolicy Bypass -File $script
    
    # Parse output for pass/fail counts
    $passes = ($output | Select-String -Pattern "\[PASS\]").Count
    $failures = ($output | Select-String -Pattern "\[FAIL\]").Count
    
    $totalTests += ($passes + $failures)
    $passedTests += $passes
    
    if ($failures -gt 0) {
        $hasFailures = $true
        Write-Host " -> $script had $failures failures!" -ForegroundColor Red
        # Print output lines that contain FAIL
        $output | Select-String -Pattern "\[FAIL\]" | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host " -> $script passed successfully ($passes tests)." -ForegroundColor Green
    }
    Write-Host "---------------------------------------------"
}

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Master E2E Verification Complete           " -ForegroundColor Cyan
Write-Host "  Total Tests Run: $totalTests" -ForegroundColor Cyan
Write-Host "  Passed: $passedTests" -ForegroundColor Green
if ($hasFailures) {
    Write-Host "  Failed: $($totalTests - $passedTests)" -ForegroundColor Red
    exit 1
} else {
    Write-Host "  All tests passed successfully!" -ForegroundColor Green
    exit 0
}
