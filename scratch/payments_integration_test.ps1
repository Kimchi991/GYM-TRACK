# GymTrackPro Payments & Subscription Lifecycle E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Payments & Lifecycle E2E Test   " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 0. Clean DB
Write-Host "Cleaning Database..." -ForegroundColor Yellow
$cleanQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; DELETE FROM AttendanceLogs; DELETE FROM Notifications; DELETE FROM WalkInVisitors; DELETE FROM GymInvitations; DELETE FROM Payments; DELETE FROM MembershipPauses; DELETE FROM Subscriptions; DELETE FROM Members; DELETE FROM MembershipPlans; DELETE FROM AuditLogs; DELETE FROM Users;"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$cleanQuery" | Out-Null
Write-Host "Database cleaned successfully." -ForegroundColor Green

# 1. Start the API in the background
Write-Host "Starting ASP.NET Core API..." -ForegroundColor Yellow
$apiProcess = Start-Process dotnet -ArgumentList "run --project src/GymTrackPro.API" -WorkingDirectory $PWD -PassThru -NoNewWindow -RedirectStandardOutput "api_payments_integration_test.log" -RedirectStandardError "api_payments_integration_test_error.log"

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

# --- Setup Users & Metadata ---
Write-Host "Setting up test users, members, and plans..." -ForegroundColor Yellow

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

# Login
$adminLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="admin_user"; Password="SecurePassword@123"}) -Headers $headers
$adminToken = $adminLogin.data.token
$adminHeaders = @{ "Content-Type" = "application/json"; "Authorization" = "Bearer $adminToken" }

$recepLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="recep_user"; Password="SecurePassword@123"}) -Headers $headers
$recepToken = $recepLogin.data.token
$recepHeaders = @{ "Content-Type" = "application/json"; "Authorization" = "Bearer $recepToken" }

# Create Plan
$planBody = ConvertTo-Json @{ planName="Gold VIP"; durationDays=30; price=1500.00; description="VIP access" }
$planRes = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $planBody -Headers $adminHeaders
$planId = $planRes.data.planID

# Create Member
$memberBody = ConvertTo-Json @{
    firstName = "Bob"
    lastName = "Miller"
    gender = "Male"
    birthDate = "1990-01-01T00:00:00Z"
    phoneNumber = "+639170001111"
    email = "bob.miller@example.com"
    emergencyContact = "911"
}
$memberRes = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $memberBody -Headers $recepHeaders
$memberId = $memberRes.data.memberID
$qrCode = $memberRes.data.qrCode

Write-Host "Test setup complete! Running test suite..." -ForegroundColor Green

# --- Case 1: Subscribe Member (Status = PendingPayment) ---
$subId = 0
Assert-Test "Subscribe Member: Starts as PendingPayment" {
    $body = ConvertTo-Json @{
        MemberID = $memberId
        PlanID = $planId
        StartDate = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/subscriptions" -Method Post -Body $body -Headers $recepHeaders
    $global:subId = $res.data.subscriptionID
    $res.success -eq $true -and $res.data.status -eq "PendingPayment"
}

# --- Case 2: Reject Check-In on Pending Subscription ---
Assert-Test "Check-In: Rejected for PendingPayment Status" {
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body (ConvertTo-Json $qrCode) -Headers $recepHeaders
    } catch {
        if ($_.Exception.Response.StatusCode -eq "BadRequest") {
            $rejected = $true
        }
    }
    $rejected
}

# --- Case 3: Reject Online Payment without Reference Number ---
Assert-Test "Payment: Reject GCash without Reference Number" {
    $body = ConvertTo-Json @{
        memberID = $memberId
        subscriptionID = $subId
        amount = 1500.00
        discount = 0.00
        paymentMethod = "GCash"
        paymentStatus = "Paid"
        referenceNumber = ""
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body $body -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*Reference number is required*"
    }
    $rejected
}

# --- Case 4: Process Valid Payment (Paid -> Activates Subscription) ---
$paymentId = 0
$receiptNumber = ""
Assert-Test "Payment: Successful Online Payment activates subscription" {
    $body = ConvertTo-Json @{
        memberID = $memberId
        subscriptionID = $subId
        amount = 1500.00
        discount = 100.00
        paymentMethod = "GCash"
        paymentStatus = "Paid"
        referenceNumber = "TXN-748392"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body $body -Headers $recepHeaders
    $global:paymentId = $res.data.paymentID
    $global:receiptNumber = $res.data.receiptNumber
    
    # Fetch subscription to verify it became Active
    $subCheck = Invoke-RestMethod -Uri "$baseUrl/subscriptions/$subId" -Method Get -Headers $recepHeaders
    
    $res.success -eq $true -and $res.data.finalAmount -eq 1400.00 -and $subCheck.data.status -eq "Active"
}

# --- Case 5: Verify Check-In Allowed Now ---
Assert-Test "Check-In: Successful now that subscription is Active" {
    $res = Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body (ConvertTo-Json $qrCode) -Headers $recepHeaders
    $res.success -eq $true
}

# --- Case 6: Reject Duplicate Reference Number ---
Assert-Test "Payment: Reject duplicate reference number" {
    $body = ConvertTo-Json @{
        memberID = $memberId
        subscriptionID = $subId
        amount = 1500.00
        discount = 0.00
        paymentMethod = "GCash"
        paymentStatus = "Paid"
        referenceNumber = "TXN-748392"
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body $body -Headers $recepHeaders
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $rejected = $errBody.message -like "*reference number already exists*"
    }
    $rejected
}

# --- Case 7: Search Payments ---
Assert-Test "Search Payments: Filter by status and receipt" {
    $res = Invoke-RestMethod -Uri "$baseUrl/payments/search?status=Paid&receiptNumber=$receiptNumber" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $res.data.Count -eq 1
}

# --- Case 8: Pause Subscription ---
Assert-Test "Pause Subscription: Updates status to Paused" {
    $body = ConvertTo-Json @{ reason = "Medical issues" }
    Invoke-RestMethod -Uri "$baseUrl/subscriptions/$subId/pause" -Method Post -Body $body -Headers $recepHeaders | Out-Null
    
    # Verify status
    $subCheck = Invoke-RestMethod -Uri "$baseUrl/subscriptions/$subId" -Method Get -Headers $recepHeaders
    $subCheck.data.status -eq "Paused"
}

# --- Case 9: Reject Check-In on Paused Subscription ---
Assert-Test "Check-In: Rejected while subscription is Paused" {
    # Check out from today's previous check-in first so we aren't blocked by double check-in lock
    $activeLogs = Invoke-RestMethod -Uri "$baseUrl/attendance/member/$memberId" -Method Get -Headers $recepHeaders
    $logId = $activeLogs.data[0].attendanceID
    Invoke-RestMethod -Uri "$baseUrl/attendance/$logId/checkout" -Method Post -Headers $recepHeaders | Out-Null

    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body (ConvertTo-Json $qrCode) -Headers $recepHeaders
    } catch {
        if ($_.Exception.Response.StatusCode -eq "BadRequest") {
            $rejected = $true
        }
    }
    $rejected
}

# --- Case 10: Resume Subscription with Extension ---
Assert-Test "Resume Subscription: Restores status and extends EndDate" {
    $beforeSub = Invoke-RestMethod -Uri "$baseUrl/subscriptions/$subId" -Method Get -Headers $recepHeaders
    $beforeEndDate = [DateTime]$beforeSub.data.endDate

    # Pause it for a moment, then resume
    Start-Sleep -Seconds 1
    Invoke-RestMethod -Uri "$baseUrl/subscriptions/$subId/resume" -Method Post -Headers $recepHeaders | Out-Null
    
    $afterSub = Invoke-RestMethod -Uri "$baseUrl/subscriptions/$subId" -Method Get -Headers $recepHeaders
    $afterEndDate = [DateTime]$afterSub.data.endDate

    $afterSub.data.status -eq "Active" -and $afterEndDate -gt $beforeEndDate
}

# --- Case 11: Refund Payment (Admin Only) ---
Assert-Test "Refund Payment: Status becomes Refunded, Subscription becomes Cancelled" {
    $res = Invoke-RestMethod -Uri "$baseUrl/payments/$paymentId/refund" -Method Post -Headers $adminHeaders
    
    # Verify sub is Cancelled
    $subCheck = Invoke-RestMethod -Uri "$baseUrl/subscriptions/$subId" -Method Get -Headers $recepHeaders
    
    $res.success -eq $true -and $res.data.paymentStatus -eq "Refunded" -and $subCheck.data.status -eq "Cancelled"
}

# --- Case 12: Block Receptionist from Refunding ---
Assert-Test "RBAC: Block Receptionist from Refund" {
    # Create a fresh subscription + payment for this RBAC test (the previous one was already refunded)
    $newSubRes = Invoke-RestMethod -Uri "$baseUrl/subscriptions" -Method Post -Body (ConvertTo-Json @{ MemberID=$memberId; PlanID=$planId }) -Headers $adminHeaders
    $newSubId = $newSubRes.data.subscriptionID
    $newPayRes = Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body (ConvertTo-Json @{
        memberID = $memberId; subscriptionID = $newSubId
        amount = 1500.00; discount = 0.00
        paymentMethod = "Cash"; paymentStatus = "Paid"; referenceNumber = ""
    }) -Headers $adminHeaders
    $newPayId = $newPayRes.data.paymentID

    $blocked = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/payments/$newPayId/refund" -Method Post -Headers $recepHeaders
    } catch {
        if ($_.Exception.Response.StatusCode -eq "Forbidden") {
            $blocked = $true
        }
    }
    $blocked
}

# --- Case 13: Audit Logs Verification ---
Assert-Test "Audit Logging: Verify Payments and Lifecycle logs written" {
    $logQuery = "SET NOCOUNT ON; SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('Subscription Initialized', 'Payment Completed', 'Subscription Activated', 'Subscription Paused', 'Subscription Resumed', 'Payment Refunded');"
    $logCount = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$logQuery").Trim()
    [int]$logCount -ge 6
}

# Stop API
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Payments & Lifecycle E2E Summary             " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
