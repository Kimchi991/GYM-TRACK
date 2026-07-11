# GymTrackPro System Settings E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro System Settings E2E Test       " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 0. Clean DB
Write-Host "Cleaning Database..." -ForegroundColor Yellow
$cleanQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; DELETE FROM AttendanceLogs; DELETE FROM Notifications; DELETE FROM WalkInVisitors; DELETE FROM GymInvitations; DELETE FROM Payments; DELETE FROM MembershipPauses; DELETE FROM Subscriptions; DELETE FROM Members; DELETE FROM MembershipPlans; DELETE FROM AuditLogs; DELETE FROM Users;"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$cleanQuery" | Out-Null
Write-Host "Database cleaned successfully." -ForegroundColor Green

# 1. Start the API in the background (will trigger EF seed for Settings)
Write-Host "Starting ASP.NET Core API..." -ForegroundColor Yellow
$apiProcess = Start-Process dotnet -ArgumentList "run --project src/GymTrackPro.API" -WorkingDirectory $PWD -PassThru -NoNewWindow -RedirectStandardOutput "api_settings_integration_test.log" -RedirectStandardError "api_settings_integration_test_error.log"

# Wait for API to respond
$apiReady = $false
for ($i = 1; $i -le 15; $i++) {
    try {
        $ping = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="ping"; Password="ping"}) -Headers $headers -ErrorAction Stop
    } catch {
        if ($_.Exception.Response.StatusCode -eq "Unauthorized" -or $_.Exception.Response.StatusCode -eq "BadRequest") {
            $apiReady = $true
            break;
        }
    }
    Write-Host "Waiting for API to start (attempt $i/15)..."
    Start-Sleep -Seconds 2
}

if (-not $apiReady) {
    Write-Host "API failed to start or respond." -ForegroundColor Red
    Stop-Process -Id $apiProcess.Id -Force
    exit 1
}
Write-Host "API is up and running!" -ForegroundColor Green

$testResults = [System.Collections.Generic.List[string]]::new()

function Assert-Test {
    param(
        [string]$name,
        [scriptblock]$testBlock
    )
    try {
        $result = &$testBlock
        if ($result) {
            Write-Host "[PASS] $name" -ForegroundColor Green
            $testResults.Add("[PASS] $name")
        } else {
            Write-Host "[FAIL] $name" -ForegroundColor Red
            $testResults.Add("[FAIL] $name")
        }
    } catch {
        Write-Host "[FAIL] $name (Exception: $_)" -ForegroundColor Red
        $testResults.Add("[FAIL] $name")
    }
}

# --- Setup Users ---
Write-Host "Setting up users..." -ForegroundColor Yellow

# Register Admin
$adminReg = ConvertTo-Json @{
    Username = "settings_admin"
    Email = "admin.settings@gymtrack.pro"
    Password = "SecurePassword@123"
    FirstName = "Settings"
    LastName = "Admin"
}
$res1 = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $adminReg -Headers $headers
$updateAdmin = "SET QUOTED_IDENTIFIER ON; UPDATE Users SET EmailVerified=1, Role=0 WHERE Username='settings_admin';"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$updateAdmin" | Out-Null

# Login Admin
$adminLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="settings_admin"; Password="SecurePassword@123"}) -Headers $headers
$adminToken = $adminLogin.data.token
$adminHeaders = @{ "Content-Type" = "application/json"; "Authorization" = "Bearer $adminToken" }

# Register Receptionist
$recepReg = ConvertTo-Json @{
    Username = "settings_recep"
    Email = "recep.settings@gymtrack.pro"
    Password = "SecurePassword@123"
    FirstName = "Settings"
    LastName = "Recep"
}
$res2 = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $recepReg -Headers $headers
$updateRecep = "SET QUOTED_IDENTIFIER ON; UPDATE Users SET EmailVerified=1, Role=1 WHERE Username='settings_recep';"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$updateRecep" | Out-Null

# Login Receptionist
$recepLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="settings_recep"; Password="SecurePassword@123"}) -Headers $headers
$recepToken = $recepLogin.data.token
$recepHeaders = @{ "Content-Type" = "application/json"; "Authorization" = "Bearer $recepToken" }

Write-Host "Users set up completed. Running Settings E2E checks..." -ForegroundColor Green

# --- Dynamic date range ---
$startDate = (Get-Date).AddDays(-1).ToString("yyyy-MM-dd")
$endDate   = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")

# --- Case 1: Retrieve All Settings ---
Assert-Test "Settings: Retrieve All Settings successfully" {
    $res = Invoke-RestMethod -Uri "$baseUrl/settings" -Method Get -Headers $adminHeaders
    $res.success -eq $true -and $res.data.Count -eq 10 -and $res.data[0].settingKey -ne $null
}

# --- Case 2: Update Setting (Admin Authorized) ---
Assert-Test "Settings: Update QRPrefix successfully as Administrator" {
    $body = ConvertTo-Json @{ SettingValue = "GTP-NEW-" }
    $res = Invoke-RestMethod -Uri "$baseUrl/settings/QRPrefix" -Method Put -Body $body -Headers $adminHeaders
    
    # Fetch again to verify value
    $verify = Invoke-RestMethod -Uri "$baseUrl/settings" -Method Get -Headers $adminHeaders
    $qrSetting = $verify.data | Where-Object { $_.settingKey -eq "QRPrefix" }
    
    $res.success -eq $true -and $qrSetting.settingValue -eq "GTP-NEW-"
}

# --- Case 3: Update Setting (Receptionist Forbidden) ---
Assert-Test "Settings: Update QRPrefix fails as Receptionist (Forbidden)" {
    $body = ConvertTo-Json @{ SettingValue = "GTP-BAD-" }
    $forbidden = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/settings/QRPrefix" -Method Put -Body $body -Headers $recepHeaders
    } catch {
        if ($_.Exception.Response.StatusCode -eq "Forbidden") {
            $forbidden = $true
        }
    }
    $forbidden -eq $true
}

# --- Case 4: Verify Audit Logging ---
Assert-Test "Settings: Verify Audit Logs records configuration modifications" {
    $logsRes = Invoke-RestMethod -Uri "$baseUrl/reports/cashier-activity?startDate=$startDate&endDate=$endDate" -Method Get -Headers $adminHeaders
    $match = $logsRes.data | Where-Object { $_.action -eq "System Setting Modified" -and $_.details -like "*GTP-NEW-*" }
    $match -ne $null
}

# --- Case 5: Verify Settings Influence on Member Creation (QR prefix test) ---
Assert-Test "Settings: Member creation respects custom dynamic QRPrefix setting" {
    $memberReg = ConvertTo-Json @{
        firstName = "John"
        lastName = "SettingsTest"
        gender = "Male"
        birthDate = "1990-01-01T00:00:00Z"
        phoneNumber = "+639170001111"
        emergencyContact = "911"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $memberReg -Headers $adminHeaders
    $res.success -eq $true -and $res.data.qrCode -like "GTP-NEW-*"
}

# Stop API
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " System Settings E2E Summary                 " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
