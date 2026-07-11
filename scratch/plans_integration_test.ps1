# GymTrackPro Membership Plans E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Membership Plans E2E Test       " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 0. Clean DB
Write-Host "Cleaning Database..." -ForegroundColor Yellow
$cleanQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; DELETE FROM AttendanceLogs; DELETE FROM Notifications; DELETE FROM WalkInVisitors; DELETE FROM GymInvitations; DELETE FROM Payments; DELETE FROM MembershipPauses; DELETE FROM Subscriptions; DELETE FROM Members; DELETE FROM MembershipPlans; DELETE FROM AuditLogs; DELETE FROM Users;"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$cleanQuery" | Out-Null
Write-Host "Database cleaned successfully." -ForegroundColor Green

# 1. Start the API in the background
Write-Host "Starting ASP.NET Core API..." -ForegroundColor Yellow
$apiProcess = Start-Process dotnet -ArgumentList "run --project src/GymTrackPro.API" -WorkingDirectory $PWD -PassThru -NoNewWindow -RedirectStandardOutput "api_plans_integration_test.log" -RedirectStandardError "api_plans_integration_test_error.log"

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
Write-Host "Setting up test users..." -ForegroundColor Yellow

# Register Admin
$adminReg = ConvertTo-Json @{
    Username = "admin_user"
    Email = "admin@gymtrack.pro"
    Password = "SecurePassword@123"
    FirstName = "Admin"
    LastName = "User"
}
$res1 = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $adminReg -Headers $headers
$updateAdmin = "SET QUOTED_IDENTIFIER ON; UPDATE Users SET EmailVerified=1, Role=0 WHERE Username='admin_user';"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$updateAdmin" | Out-Null

# Register Receptionist
$recepReg = ConvertTo-Json @{
    Username = "recep_user"
    Email = "recep@gymtrack.pro"
    Password = "SecurePassword@123"
    FirstName = "Receptionist"
    LastName = "User"
}
$res2 = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $recepReg -Headers $headers
$updateRecep = "SET QUOTED_IDENTIFIER ON; UPDATE Users SET EmailVerified=1, Role=1 WHERE Username='recep_user';"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$updateRecep" | Out-Null

# Login and Get Tokens
$adminLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="admin_user"; Password="SecurePassword@123"}) -Headers $headers
$adminToken = $adminLogin.data.token
$adminHeaders = @{
    "Content-Type" = "application/json"
    "Authorization" = "Bearer $adminToken"
}

$recepLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="recep_user"; Password="SecurePassword@123"}) -Headers $headers
$recepToken = $recepLogin.data.token
$recepHeaders = @{
    "Content-Type" = "application/json"
    "Authorization" = "Bearer $recepToken"
}

Write-Host "Test setup complete! Running test suite..." -ForegroundColor Green

# --- Case 1: Valid Plan Creation (Admin) ---
$planIdA = 0
Assert-Test "Create Plan: Standard Monthly" {
    $body = ConvertTo-Json @{
        planName = "Standard Monthly"
        durationDays = 30
        price = 49.99
        description = "Standard access for 30 days"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $body -Headers $adminHeaders
    $global:planIdA = $res.data.planID
    $res.success -eq $true -and $res.data.planName -eq "Standard Monthly" -and $res.data.status -eq "Active"
}

# --- Case 2: Reject Duplicate Plan Name ---
Assert-Test "Create Plan: Reject Duplicate Name" {
    $body = ConvertTo-Json @{
        planName = "Standard Monthly"
        durationDays = 60
        price = 89.99
        description = "Should fail due to duplicate name"
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $body -Headers $adminHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*already exists*"
    }
    $rejected
}

# --- Case 3: Get Plan By ID ---
Assert-Test "Get Plan by ID" {
    $res = Invoke-RestMethod -Uri "$baseUrl/plans/$planIdA" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $res.data.planName -eq "Standard Monthly"
}

# --- Case 4: Get All Plans ---
Assert-Test "Get All Plans" {
    $res = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $res.data.Count -ge 1
}

# --- Case 5: Update Plan Details ---
Assert-Test "Update Plan Details" {
    $body = ConvertTo-Json @{
        planName = "Standard Monthly Pro"
        durationDays = 30
        price = 59.99
        description = "Upgraded Standard access"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/plans/$planIdA" -Method Put -Body $body -Headers $adminHeaders
    $res.success -eq $true -and $res.data.planName -eq "Standard Monthly Pro" -and $res.data.price -eq 59.99
}

# --- Case 6: Reject Duplicate Name on Update ---
$planIdB = 0
Assert-Test "Create Plan B for Update Conflict Test" {
    $body = ConvertTo-Json @{
        planName = "VIP Unlimited"
        durationDays = 365
        price = 499.99
        description = "Annual VIP access"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $body -Headers $adminHeaders
    $global:planIdB = $res.data.planID
    $res.success -eq $true
}

Assert-Test "Update Plan: Reject Duplicate Name Conflict" {
    $body = ConvertTo-Json @{
        planName = "Standard Monthly Pro"
        durationDays = 365
        price = 499.99
        description = "Should fail because Plan A already uses this name"
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/plans/$planIdB" -Method Put -Body $body -Headers $adminHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*already exists*"
    }
    $rejected
}

# --- Case 7: Soft-Deactivate Plan (Delete) ---
Assert-Test "Delete Plan (Mark Inactive)" {
    $res = Invoke-RestMethod -Uri "$baseUrl/plans/$planIdA" -Method Delete -Headers $adminHeaders
    
    # Fetch again to verify status is "Inactive"
    $check = Invoke-RestMethod -Uri "$baseUrl/plans/$planIdA" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $check.data.status -eq "Inactive"
}

# --- Case 8: RBAC Restriction Verification ---
Assert-Test "RBAC: Block Receptionist from Creating Plans" {
    $body = ConvertTo-Json @{
        planName = "Receptionist Plan"
        durationDays = 10
        price = 10.00
        description = "Should fail authorization"
    }
    $blocked = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $body -Headers $recepHeaders
    } catch {
        if ($_.Exception.Response.StatusCode -eq "Forbidden") {
            $blocked = $true
        }
    }
    $blocked
}

# --- Case 9: Audit Logs Verification ---
Assert-Test "Audit Logging: Verify Plan Logs Written" {
    $logQuery = "SET NOCOUNT ON; SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('Plan Created', 'Plan Updated', 'Plan Deleted');"
    $logCount = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$logQuery").Trim()
    [int]$logCount -ge 3
}

# Stop the API process
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Plans Integration Test Results Summary       " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
