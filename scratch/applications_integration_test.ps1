# GymTrackPro Membership Applications E2E Integration Test
# Assumes API runs on http://localhost:5221
# Connects directly to the SQL Server database via ADO.NET using appsettings.Development.json connection string.

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Membership Applications E2E Test" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# Parse connection string from appsettings.Development.json
$configPath = Join-Path $PWD "src/GymTrackPro.API/appsettings.Development.json"
if (-not (Test-Path $configPath)) {
    Write-Host "Configuration file appsettings.Development.json not found." -ForegroundColor Red
    exit 1
}

$config = Get-Content $configPath | ConvertFrom-Json
$connString = $config.ConnectionStrings.DefaultConnection

if ([string]::IsNullOrWhiteSpace($connString)) {
    Write-Host "Connection string not found in appsettings.Development.json." -ForegroundColor Red
    exit 1
}

# Helper function to execute SQL commands directly
function Execute-Sql {
    param(
        [string]$query
    )
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connString
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $result = $command.ExecuteNonQuery()
    $connection.Close()
    return $result
}

# Helper function to execute scalar SQL commands
function Execute-SqlScalar {
    param(
        [string]$query
    )
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connString
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $result = $command.ExecuteScalar()
    $connection.Close()
    return $result
}

# 0. Clean DB tables for test run using correct dependency order
Write-Host "Cleaning applications database records..." -ForegroundColor Yellow
try {
    $cleanup = @"
    DELETE FROM AuditLogs;
    DELETE FROM WorkoutLogs;
    DELETE FROM WorkoutRoutines;
    DELETE FROM TrainerClients;
    DELETE FROM AttendanceAdjustments;
    DELETE FROM AttendanceOperations;
    DELETE FROM AttendanceLogs;
    DELETE FROM Payments;
    DELETE FROM Subscriptions;
    DELETE FROM MemberProjectionVersions;
    DELETE FROM AccountInvites;
    DELETE FROM MemberApplications;
    DELETE FROM Users;
    DELETE FROM Members;
    DELETE FROM WalkInVisitors;
    DELETE FROM MembershipPlans;
"@
    Execute-Sql $cleanup | Out-Null
    Write-Host "Database tables cleaned successfully." -ForegroundColor Green
} catch {
    Write-Host "Warning: SQL cleaning failed: $_" -ForegroundColor Yellow
}

# 1. Start the API in the background
Write-Host "Starting ASP.NET Core API..." -ForegroundColor Yellow
$env:ASPNETCORE_ENVIRONMENT = "Development"
$apiProcess = Start-Process dotnet -ArgumentList ".\bin\Debug\net10.0\GymTrackPro.API.dll --environment Development --urls http://localhost:5221" -WorkingDirectory "$PWD\src\GymTrackPro.API" -PassThru -WindowStyle Hidden

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

# --- Setup Users & Plans ---
Write-Host "Setting up test users and plans..." -ForegroundColor Yellow

# Insert Receptionist directly in the DB
$insertRecep = @"
INSERT INTO Users (Username, Email, NormalizedEmail, FirebaseUid, FirstName, LastName, Role, EmailVerified, IsActive, CreatedAt, UpdatedAt)
VALUES ('recep_user', 'recep@gymtrack.pro', 'recep@gymtrack.pro', 'recep_firebase_uid', 'Receptionist', 'User', 1, 1, 1, GETUTCDATE(), GETUTCDATE());
"@
Execute-Sql $insertRecep | Out-Null

# Configure receptionist request headers with the test auth bypass keys
$recepHeaders = @{
    "Content-Type" = "application/json"
    "X-Test-User-Uid" = "recep_firebase_uid"
    "X-Test-User-Email" = "recep@gymtrack.pro"
}

# Create a Membership Plan directly in DB
Execute-Sql "INSERT INTO MembershipPlans (PlanName, DurationDays, Price, Status, LastModified) VALUES ('Monthly Pass', 30, 999.00, 'Active', GETUTCDATE());" | Out-Null
$planId = [int](Execute-SqlScalar "SELECT TOP 1 PlanID FROM MembershipPlans WHERE PlanName = 'Monthly Pass';")

Write-Host "Setup complete. Running application verification test suite..." -ForegroundColor Green

# --- Case 1: Submit One-Day Pass Application ---
$appId1 = 0
Assert-Test "Submit Application: One-Day Pass" {
    $body = ConvertTo-Json @{
        fullName = "Alice Smith"
        contactNumber = "09171112222"
        emailAddress = "alice@example.com"
        isOneDayPass = $true
        paymentMethod = "GCash"
        paymentReferenceNumber = "GC-123456"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/applications" -Method Post -Body $body -Headers $headers
    $global:appId1 = $res.data.applicationID
    $res.success -eq $true -and $res.data.selectedPlanName -eq "One-Day Pass" -and $res.data.price -eq 100.00
}

# --- Case 2: Submit Monthly Package Application ---
$appId2 = 0
Assert-Test "Submit Application: Monthly Membership" {
    $body = ConvertTo-Json @{
        fullName = "Bob Johnson"
        contactNumber = "09173334444"
        emailAddress = "bob@example.com"
        isOneDayPass = $false
        selectedPlanID = $planId
        paymentMethod = "Maya"
        paymentReferenceNumber = "MY-987654"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/applications" -Method Post -Body $body -Headers $headers
    $global:appId2 = $res.data.applicationID
    $res.success -eq $true -and $res.data.selectedPlanName -eq "Monthly Pass" -and $res.data.price -eq 999.00
}

# --- Case 3: Get Pending Queue (Receptionist) ---
Assert-Test "Get Pending Applications Queue" {
    $res = Invoke-RestMethod -Uri "$baseUrl/applications/pending" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $res.data.Count -eq 2
}

# --- Case 4: Verify/Approve One-Day Pass (creates WalkInVisitor) ---
Assert-Test "Approve One-Day Pass Application" {
    $body = ConvertTo-Json @{
        status = "Approved"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/applications/$appId1/verify" -Method Post -Body $body -Headers $recepHeaders
    
    # Check DB if WalkInVisitor was logged
    $visitorCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM WalkInVisitors WHERE VisitorName = 'Alice Smith';")
    
    $res.success -eq $true -and $res.data.applicationStatus -eq "Approved" -and $visitorCount -eq 1
}

# --- Case 5: Verify/Approve Membership (creates Member, Subscription, Payment, Invite) ---
Assert-Test "Approve Monthly Membership Application" {
    $body = ConvertTo-Json @{
        status = "Approved"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/applications/$appId2/verify" -Method Post -Body $body -Headers $recepHeaders
    
    # Check DB if Member, Subscription, Payment, and Invite were created
    $memberCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM Members WHERE Email = 'bob@example.com';")
    $subCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM Subscriptions WHERE Status = 'Active';")
    $paymentCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM Payments WHERE ReferenceNumber = 'MY-987654';")
    $inviteCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM AccountInvites WHERE NormalizedEmail = 'BOB@EXAMPLE.COM';")
    
    $res.success -eq $true -and $memberCount -eq 1 -and $subCount -eq 1 -and $paymentCount -eq 1 -and $inviteCount -eq 1
}

# --- Case 6: Get Dashboard Metrics (Occupancy & Plan Distribution) ---
Assert-Test "Verify Dashboard Metrics Counts" {
    $res = Invoke-RestMethod -Uri "$baseUrl/dashboard/metrics" -Method Get -Headers $recepHeaders
    
    # We should have 0 checked-in (no check-ins occurred), but 1 active membership and 1 plan distribution record
    $res.success -eq $true -and $res.data.activeMembershipsCount -eq 1 -and $res.data.membershipPlanDistribution.Count -ge 1 -and $res.data.pendingApplicationsCount -eq 0
}

# Stop the API process
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Applications Integration Test Results Summary" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
