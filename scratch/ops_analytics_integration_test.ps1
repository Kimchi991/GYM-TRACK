# GymTrackPro Dashboard & Operations Reports E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Dashboard & Reports E2E Test    " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 0. Clean DB
Write-Host "Cleaning Database..." -ForegroundColor Yellow
$cleanQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; DELETE FROM AttendanceLogs; DELETE FROM Payments; DELETE FROM Subscriptions; DELETE FROM Members; DELETE FROM MembershipPlans; DELETE FROM AuditLogs; DELETE FROM Users;"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$cleanQuery" | Out-Null
Write-Host "Database cleaned successfully." -ForegroundColor Green

# 1. Start the API in the background
Write-Host "Starting ASP.NET Core API..." -ForegroundColor Yellow
$apiProcess = Start-Process dotnet -ArgumentList "run --project src/GymTrackPro.API" -WorkingDirectory $PWD -PassThru -WindowStyle Hidden

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

# --- Setup Users & Data ---
Write-Host "Setting up seed records..." -ForegroundColor Yellow

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

# Login
$adminLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="admin_user"; Password="SecurePassword@123"}) -Headers $headers
$adminToken = $adminLogin.data.token
$adminHeaders = @{ "Content-Type" = "application/json"; "Authorization" = "Bearer $adminToken" }

# Create Plan
$planRes = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body (ConvertTo-Json @{ planName="Platinum Plus"; durationDays=30; price=2000.00; description="VIP" }) -Headers $adminHeaders
$planId = $planRes.data.planID

# Create Member
$memberRes = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body (ConvertTo-Json @{ firstName="Alice"; lastName="Walker"; gender="Female"; birthDate="1995-05-15T00:00:00Z"; phoneNumber="+639170002222"; email="alice.w@example.com"; emergencyContact="117" }) -Headers $adminHeaders
$memberId = $memberRes.data.memberID
$qrCode = $memberRes.data.qrCode

# Create Subscription
$subRes = Invoke-RestMethod -Uri "$baseUrl/subscriptions" -Method Post -Body (ConvertTo-Json @{ MemberID=$memberId; PlanID=$planId; StartDate=(Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ") }) -Headers $adminHeaders
$subId = $subRes.data.subscriptionID

# Process Payment
$payRes = Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body (ConvertTo-Json @{ memberID=$memberId; subscriptionID=$subId; amount=2000.00; discount=200.00; paymentMethod="GCash"; paymentStatus="Paid"; referenceNumber="TXN-A1B2C3" }) -Headers $adminHeaders
$paymentId = $payRes.data.paymentID

# Check-in and out
$checkinRes = Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body (ConvertTo-Json $qrCode) -Headers $adminHeaders
$attendanceId = $checkinRes.data.attendanceID
Invoke-RestMethod -Uri "$baseUrl/attendance/$attendanceId/checkout" -Method Post -Headers $adminHeaders | Out-Null

# Create a refunded transaction to seed refunds data
$subRes2 = Invoke-RestMethod -Uri "$baseUrl/subscriptions" -Method Post -Body (ConvertTo-Json @{ MemberID=$memberId; PlanID=$planId; StartDate=(Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ") }) -Headers $adminHeaders
$subId2 = $subRes2.data.subscriptionID
$payRes2 = Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body (ConvertTo-Json @{ memberID=$memberId; subscriptionID=$subId2; amount=2000.00; discount=0.00; paymentMethod="Cash"; paymentStatus="Paid"; referenceNumber="" }) -Headers $adminHeaders
$paymentId2 = $payRes2.data.paymentID
Invoke-RestMethod -Uri "$baseUrl/payments/$paymentId2/refund" -Method Post -Headers $adminHeaders | Out-Null

Write-Host "Seed completed successfully. Running analytics E2E checks..." -ForegroundColor Green

# --- Case 1: Dashboard Metrics JSON Query ---
Assert-Test "Dashboard: Fetch Metrics successfully" {
    $res = Invoke-RestMethod -Uri "$baseUrl/dashboard/metrics" -Method Get -Headers $adminHeaders
    $res.success -eq $true -and 
    $res.data.activeMembershipsCount -eq 1 -and 
    $res.data.revenueToday -eq 1800.00 -and 
    $res.data.newRegistrationsCount -eq 1 -and
    $res.data.revenueByPlan.Count -ge 1
}

# --- Case 2: Daily Revenue JSON & CSV Export ---
Assert-Test "Reports: Daily Revenue JSON and CSV Export" {
    $jsonRes = Invoke-RestMethod -Uri "$baseUrl/reports/daily-revenue?startDate=2026-07-01&endDate=2026-07-03" -Method Get -Headers $adminHeaders
    
    $req = [System.Net.HttpWebRequest]::Create("$baseUrl/reports/daily-revenue/export?startDate=2026-07-01&endDate=2026-07-03")
    $req.Headers.Add("Authorization", "Bearer $adminToken")
    $resp = $req.GetResponse()
    $contentType = $resp.ContentType
    $resp.Close()

    $jsonRes.success -eq $true -and $contentType -like "*text/csv*"
}

# --- Case 3: Monthly Revenue Report ---
Assert-Test "Reports: Monthly Revenue Query" {
    $res = Invoke-RestMethod -Uri "$baseUrl/reports/monthly-revenue?startDate=2026-07-01&endDate=2026-07-03" -Method Get -Headers $adminHeaders
    $res.success -eq $true -and $res.data[0].month -eq "2026-07"
}

# --- Case 4: Attendance Report ---
Assert-Test "Reports: Attendance Query and Export" {
    $jsonRes = Invoke-RestMethod -Uri "$baseUrl/reports/attendance?startDate=2026-07-01&endDate=2026-07-03" -Method Get -Headers $adminHeaders
    
    $req = [System.Net.HttpWebRequest]::Create("$baseUrl/reports/attendance/export?startDate=2026-07-01&endDate=2026-07-03")
    $req.Headers.Add("Authorization", "Bearer $adminToken")
    $resp = $req.GetResponse()
    $contentType = $resp.ContentType
    $resp.Close()

    $jsonRes.success -eq $true -and $jsonRes.data[0].memberName -eq "Alice Walker" -and $contentType -like "*text/csv*"
}

# --- Case 5: Membership Sales Report ---
Assert-Test "Reports: Membership Sales Query" {
    $res = Invoke-RestMethod -Uri "$baseUrl/reports/membership-sales?startDate=2026-07-01&endDate=2026-07-03" -Method Get -Headers $adminHeaders
    $res.success -eq $true -and $res.data.Count -eq 1 -and $res.data[0].finalAmount -eq 1800.00
}

# --- Case 6: Expiring Memberships Report ---
Assert-Test "Reports: Expiring Memberships Query" {
    $res = Invoke-RestMethod -Uri "$baseUrl/reports/expiring-memberships?nextDays=35" -Method Get -Headers $adminHeaders
    $res.success -eq $true -and $res.data.Count -eq 1
}

# --- Case 7: Refund Report ---
Assert-Test "Reports: Refund Query and Export" {
    $jsonRes = Invoke-RestMethod -Uri "$baseUrl/reports/refunds?startDate=2026-07-01&endDate=2026-07-03" -Method Get -Headers $adminHeaders
    
    $req = [System.Net.HttpWebRequest]::Create("$baseUrl/reports/refunds/export?startDate=2026-07-01&endDate=2026-07-03")
    $req.Headers.Add("Authorization", "Bearer $adminToken")
    $resp = $req.GetResponse()
    $contentType = $resp.ContentType
    $resp.Close()

    $jsonRes.success -eq $true -and $jsonRes.data[0].amount -eq 2000.00 -and $contentType -like "*text/csv*"
}

# --- Case 8: Cashier Activity Report ---
Assert-Test "Reports: Cashier Activity Query and Export" {
    $jsonRes = Invoke-RestMethod -Uri "$baseUrl/reports/cashier-activity?startDate=2026-07-01&endDate=2026-07-03" -Method Get -Headers $adminHeaders
    
    $req = [System.Net.HttpWebRequest]::Create("$baseUrl/reports/cashier-activity/export?startDate=2026-07-01&endDate=2026-07-03")
    $req.Headers.Add("Authorization", "Bearer $adminToken")
    $resp = $req.GetResponse()
    $contentType = $resp.ContentType
    $resp.Close()

    $jsonRes.success -eq $true -and $jsonRes.data.Count -ge 5 -and $contentType -like "*text/csv*"
}

# Stop API
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Dashboard & Reports E2E Summary             " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
