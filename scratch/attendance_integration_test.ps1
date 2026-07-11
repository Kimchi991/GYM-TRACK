# GymTrackPro Attendance E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Attendance E2E Integration Test " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 0. Clean DB
Write-Host "Cleaning Database..." -ForegroundColor Yellow
$cleanQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; DELETE FROM AttendanceLogs; DELETE FROM Notifications; DELETE FROM WalkInVisitors; DELETE FROM GymInvitations; DELETE FROM Payments; DELETE FROM MembershipPauses; DELETE FROM Subscriptions; DELETE FROM Members; DELETE FROM MembershipPlans; DELETE FROM AuditLogs; DELETE FROM Users;"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$cleanQuery" | Out-Null
Write-Host "Database cleaned successfully." -ForegroundColor Green

# 1. Start the API in the background
Write-Host "Starting ASP.NET Core API..." -ForegroundColor Yellow
$apiProcess = Start-Process dotnet -ArgumentList "run --project src/GymTrackPro.API" -WorkingDirectory $PWD -PassThru -NoNewWindow -RedirectStandardOutput "api_attendance_integration_test.log" -RedirectStandardError "api_attendance_integration_test_error.log"

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

# --- Setup Users (Admin & Receptionist) ---
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

# --- Setup Membership Plan & Members ---
Write-Host "Creating Membership Plan in Database..." -ForegroundColor Yellow
$planQuery = "SET NOCOUNT ON; INSERT INTO MembershipPlans (PlanName, DurationDays, Price, Description, Status, LastModified, GymID, IsDeleted) VALUES ('Standard Monthly', 30, 49.99, 'Standard monthly plan', 'Active', GETDATE(), 1, 0); SELECT @@IDENTITY;"
$planId = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$planQuery").Trim()
Write-Host "Created Membership Plan ID: $planId" -ForegroundColor Green

# Register 3 Members: Alice (Active Sub), Bob (Expired Sub), Charlie (No Sub)
Write-Host "Registering test members..." -ForegroundColor Yellow

$alice = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body (ConvertTo-Json @{
    FirstName = "Alice"; LastName = "Smith"; Gender = "Female"; BirthDate = "1995-05-15T00:00:00Z";
    PhoneNumber = "+1234567890"; Email = "alice@example.com"; EmergencyContact = "Contact: +1999999999"
}) -Headers $recepHeaders

$bob = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body (ConvertTo-Json @{
    FirstName = "Bob"; LastName = "Johnson"; Gender = "Male"; BirthDate = "1990-10-10T00:00:00Z";
    PhoneNumber = "+1555123456"; Email = "bob@example.com"; EmergencyContact = "Contact: +1999999999"
}) -Headers $recepHeaders

$charlie = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body (ConvertTo-Json @{
    FirstName = "Charlie"; LastName = "Smith"; Gender = "Male"; BirthDate = "2000-01-01T00:00:00Z";
    PhoneNumber = "+1777000111"; Email = "charlie@example.com"; EmergencyContact = "Contact: +1999999999"
}) -Headers $recepHeaders

# Attach subscriptions via direct SQL queries
Write-Host "Configuring member subscriptions..." -ForegroundColor Yellow

# Alice: Active subscription (starts 1 day ago, ends in 29 days)
$subAliceQuery = "INSERT INTO Subscriptions (MemberID, PlanID, StartDate, EndDate, Status, LastModified, GymID) VALUES ($($alice.data.memberID), $planId, DATEADD(day, -1, GETDATE()), DATEADD(day, 29, GETDATE()), 'Active', GETDATE(), 1);"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$subAliceQuery" | Out-Null

# Bob: Expired subscription (starts 40 days ago, ended 10 days ago)
$subBobQuery = "INSERT INTO Subscriptions (MemberID, PlanID, StartDate, EndDate, Status, LastModified, GymID) VALUES ($($bob.data.memberID), $planId, DATEADD(day, -40, GETDATE()), DATEADD(day, -10, GETDATE()), 'Active', GETDATE(), 1);"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$subBobQuery" | Out-Null

Write-Host "Test setup complete! Running test suite..." -ForegroundColor Green

# --- Case 1: Valid Check-In ---
$aliceLogId = 0
Assert-Test "Check-In: Valid Active Member" {
    # Send QR Code as raw JSON string
    $qrBody = ConvertTo-Json $($alice.data.qrCode)
    $res = Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body $qrBody -Headers $recepHeaders
    $global:aliceLogId = $res.data.attendanceID
    
    $res.success -eq $true -and $res.data.memberID -eq $($alice.data.memberID) -and $res.data.checkOutTime -eq $null
}

# --- Case 2: Double Check-In Block ---
Assert-Test "Check-In: Block Duplicate Check-In" {
    $qrBody = ConvertTo-Json $($alice.data.qrCode)
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body $qrBody -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*already checked in*"
    }
    $rejected
}

# --- Case 3: Valid Check-Out ---
Assert-Test "Check-Out: Valid Active Session" {
    $res = Invoke-RestMethod -Uri "$baseUrl/attendance/$aliceLogId/checkout" -Method Post -Headers $recepHeaders
    $res.success -eq $true
}

# --- Case 4: Double Check-Out Block ---
Assert-Test "Check-Out: Block Duplicate Check-Out" {
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/$aliceLogId/checkout" -Method Post -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*already checked out*"
    }
    $rejected
}

# --- Case 5: Daily Check-In Limit reached ---
Assert-Test "Check-In: Daily Limit Exceeded Block" {
    $qrBody = ConvertTo-Json $($alice.data.qrCode)
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body $qrBody -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*limit reached*"
    }
    $rejected
}

# --- Case 6: Expired Subscription Block ---
Assert-Test "Check-In: Reject Expired Subscription" {
    $qrBody = ConvertTo-Json $($bob.data.qrCode)
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body $qrBody -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*active subscription*"
    }
    $rejected
}

# --- Case 7: No Subscription Block ---
Assert-Test "Check-In: Reject No Subscription" {
    $qrBody = ConvertTo-Json $($charlie.data.qrCode)
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body $qrBody -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*active subscription*"
    }
    $rejected
}

# --- Case 8: Invalid QR Code Block ---
Assert-Test "Check-In: Reject Invalid QR Code" {
    $qrBody = ConvertTo-Json "GTP-INVALIDCODE99"
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body $qrBody -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*Invalid check-in code*"
    }
    $rejected
}

# --- Case 9: Audit Logs Verification ---
Assert-Test "Audit Logging: Verify Attendance Audit Logs" {
    $logQuery = "SET NOCOUNT ON; SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('CheckIn Success', 'CheckIn Failure', 'CheckOut Success');"
    $logCount = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$logQuery").Trim()
    [int]$logCount -ge 4
}

# Stop the API process
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Attendance Integration Test Results Summary  " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
