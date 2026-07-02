# GymTrackPro Notifications & Events E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Notifications & Events E2E Test" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 0. Clean DB
Write-Host "Cleaning Database..." -ForegroundColor Yellow
$cleanQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; DELETE FROM AttendanceLogs; DELETE FROM Payments; DELETE FROM Subscriptions; DELETE FROM Members; DELETE FROM MembershipPlans; DELETE FROM AuditLogs; DELETE FROM Users; DELETE FROM SystemSettings; DELETE FROM Notifications;"
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
        Write-Host "[FAIL] $name" -ForegroundColor Red
        if ($_.Exception -and $_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                Write-Host "Response Body: $body" -ForegroundColor DarkRed
            }
        } else {
            Write-Host "Exception: $_" -ForegroundColor DarkRed
        }
        $testResults.Add("[FAIL] $name")
    }
}

# --- Setup Administrator ---
Write-Host "Setting up administrator..." -ForegroundColor Yellow
$adminReg = ConvertTo-Json @{
    Username = "events_admin"
    Email = "admin.events@gymtrack.pro"
    Password = "SecurePassword@123"
    FirstName = "Events"
    LastName = "Admin"
}
$res1 = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $adminReg -Headers $headers
$updateAdmin = "SET QUOTED_IDENTIFIER ON; UPDATE Users SET EmailVerified=1, Role=0 WHERE Username='events_admin';"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$updateAdmin" | Out-Null

$adminLogin = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body (ConvertTo-Json @{Username="events_admin"; Password="SecurePassword@123"}) -Headers $headers
$adminToken = $adminLogin.data.token
$adminHeaders = @{ "Content-Type" = "application/json"; "Authorization" = "Bearer $adminToken" }

# --- Create Membership Plan for billing tests ---
$planBody = ConvertTo-Json @{
    planName = "Premium Gold Plan"
    description = "Unlimited VIP access"
    price = 2500.00
    durationDays = 30
}
$planRes = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $planBody -Headers $adminHeaders
$planId = $planRes.data.planID

# Store State Variables in script scope
$script:memberId = 0
$script:subId = 0
$script:paymentId = 0
$script:subId2 = 0

# Helper function to query notifications with polling retry to handle background queue processing latency
function Get-NotificationsWithRetry {
    param(
        [int]$memberId,
        [string]$expectedTitle,
        [int]$maxSeconds = 5
    )
    for ($i = 0; $i -lt $maxSeconds; $i++) {
        $res = Invoke-RestMethod -Uri "$baseUrl/notifications?memberId=$memberId" -Method Get -Headers $adminHeaders
        # Response might be a raw array or a count/value wrapped object depending on serialization
        $notifications = $null
        if ($res.value -ne $null) {
            $notifications = $res.value
        } else {
            $notifications = $res
        }
        $match = @($notifications) | Where-Object { $_.title -eq $expectedTitle }
        if ($match -ne $null) {
            return $notifications
        }
        Start-Sleep -Seconds 1
    }
    return @()
}

# --- Case 1: Member creation triggers welcome notifications ---
Assert-Test "Notifications: Member registration triggers Welcome notification" {
    $memberReg = ConvertTo-Json @{
        firstName = "Alice"
        lastName = "EventsTest"
        gender = "Female"
        birthDate = "1994-05-15T00:00:00Z"
        phoneNumber = "+639170002222"
        email = "alice.events@gymtrack.pro"
        emergencyContact = "117"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $memberReg -Headers $adminHeaders
    $script:memberId = $res.data.memberID
    
    # Query notifications with retry
    $notifRes = Get-NotificationsWithRetry -memberId $script:memberId -expectedTitle "Welcome to GymTrackPro!"
    $welcomeNotif = @($notifRes) | Where-Object { $_.title -eq "Welcome to GymTrackPro!" }
    
    $res.success -eq $true -and $welcomeNotif -ne $null -and $welcomeNotif.status -eq "Unread"
}

# --- Case 2: Payment Received triggers successful payment notification ---
Assert-Test "Notifications: Payment completion triggers Payment Successful notification" {
    # 1. Initialize Subscription
    $subBody = ConvertTo-Json @{
        memberID = $script:memberId
        planID = $planId
    }
    $subRes = Invoke-RestMethod -Uri "$baseUrl/subscriptions" -Method Post -Body $subBody -Headers $adminHeaders
    $script:subId = $subRes.data.subscriptionID

    # 2. Checkout Payment
    $payBody = ConvertTo-Json @{
        memberID = $script:memberId
        subscriptionID = $script:subId
        amount = 2500.00
        discount = 0
        paymentMethod = "Cash"
        paymentStatus = "Paid"
        referenceNumber = "REF-EVENT-101"
    }
    $payRes = Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body $payBody -Headers $adminHeaders
    $script:paymentId = $payRes.data.paymentID

    # Query notifications with retry
    $notifRes = Get-NotificationsWithRetry -memberId $script:memberId -expectedTitle "Payment Successful"
    $payNotif = @($notifRes) | Where-Object { $_.title -eq "Payment Successful" }

    $payRes.success -eq $true -and $payNotif -ne $null
}

# --- Case 3: Refund Processed triggers notification ---
Assert-Test "Notifications: Processing refund triggers Refund Processed notification" {
    $refRes = Invoke-RestMethod -Uri "$baseUrl/payments/$script:paymentId/refund" -Method Post -Headers $adminHeaders

    # Query notifications with retry
    $notifRes = Get-NotificationsWithRetry -memberId $script:memberId -expectedTitle "Refund Processed"
    $refNotif = @($notifRes) | Where-Object { $_.title -eq "Refund Processed" }

    $refRes.success -eq $true -and $refNotif -ne $null
}

# --- Case 4: Pause membership triggers notification ---
Assert-Test "Notifications: Pausing subscription triggers Membership Paused notification" {
    # Initialize and pay a second subscription since the first was cancelled upon refund
    $subBody2 = ConvertTo-Json @{
        memberID = $script:memberId
        planID = $planId
    }
    $subRes2 = Invoke-RestMethod -Uri "$baseUrl/subscriptions" -Method Post -Body $subBody2 -Headers $adminHeaders
    $script:subId2 = $subRes2.data.subscriptionID

    $payBody2 = ConvertTo-Json @{
        memberID = $script:memberId
        subscriptionID = $script:subId2
        amount = 2500.00
        discount = 0
        paymentMethod = "Cash"
        paymentStatus = "Paid"
        referenceNumber = "REF-EVENT-102"
    }
    $payRes2 = Invoke-RestMethod -Uri "$baseUrl/payments" -Method Post -Body $payBody2 -Headers $adminHeaders

    # Pause it
    $pauseBody = ConvertTo-Json @{ reason = "Travel plans" }
    $pauseRes = Invoke-RestMethod -Uri "$baseUrl/subscriptions/$script:subId2/pause" -Method Post -Body $pauseBody -Headers $adminHeaders

    # Query notifications with retry
    $notifRes = Get-NotificationsWithRetry -memberId $script:memberId -expectedTitle "Membership Paused"
    $pauseNotif = @($notifRes) | Where-Object { $_.title -eq "Membership Paused" }

    $pauseNotif -ne $null
}

# --- Case 5: Resume membership triggers notification ---
Assert-Test "Notifications: Resuming subscription triggers Membership Resumed notification" {
    # Extend the pause manually so resume calculations are correct
    $updateActivePause = "SET QUOTED_IDENTIFIER ON; UPDATE MembershipPauses SET PauseStartDate = DATEADD(day, -5, GETDATE()) WHERE SubscriptionID = $script:subId2;"
    docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$updateActivePause" | Out-Null

    # Call resume
    $resumeRes = Invoke-RestMethod -Uri "$baseUrl/subscriptions/$script:subId2/resume" -Method Post -Headers $adminHeaders

    # Query notifications with retry
    $notifRes = Get-NotificationsWithRetry -memberId $script:memberId -expectedTitle "Membership Resumed"
    $resumeNotif = @($notifRes) | Where-Object { $_.title -eq "Membership Resumed" }

    $resumeNotif -ne $null
}

# --- Case 6: Failed check-in triggers notification ---
Assert-Test "Notifications: Failed check-in triggers Failed Check-In Attempt notification" {
    # 1. Create a member with NO active subscriptions (will fail check-in)
    $badMemberReg = ConvertTo-Json @{
        firstName = "FailedCheckIn"
        lastName = "User"
        gender = "Male"
        birthDate = "1992-10-10T00:00:00Z"
        phoneNumber = "+639170003333"
        email = "failed.checkin@gymtrack.pro"
        emergencyContact = "911"
    }
    $badMemberRes = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $badMemberReg -Headers $adminHeaders
    $badMemberId = $badMemberRes.data.memberID
    $badMemberQR = $badMemberRes.data.qrCode
    Write-Host "Created bad member: ID=$badMemberId QR=$badMemberQR"

    # 2. Try check-in (expecting 400 Bad Request due to no active membership)
    $failedCheckIn = $false
    try {
        $qrBody = ConvertTo-Json $badMemberQR
        Write-Host "Sending checkin request with body: $qrBody"
        $checkInRes = Invoke-RestMethod -Uri "$baseUrl/attendance/checkin" -Method Post -Body $qrBody -Headers $adminHeaders
        Write-Host "Check-in succeeded unexpectedly: $(ConvertTo-Json $checkInRes)"
    } catch {
        Write-Host "Caught expected exception during check-in: $_"
        $failedCheckIn = $true
        if ($_.Exception -and $_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                Write-Host "Response Body: $body" -ForegroundColor Yellow
            }
        }
    }

    # 3. Query notifications with retry
    $notifRes = Get-NotificationsWithRetry -memberId $badMemberId -expectedTitle "Failed Check-In Attempt"
    Write-Host "Notifications for bad member: $(ConvertTo-Json $notifRes)"
    $failNotif = @($notifRes) | Where-Object { $_.title -eq "Failed Check-In Attempt" }

    $failedCheckIn -eq $true -and $failNotif -ne $null
}

# --- Case 7: Mark notification as read ---
Assert-Test "Notifications: Mark notification as read successfully" {
    # Get latest notification ID for member
    $rawRes = Invoke-RestMethod -Uri "$baseUrl/notifications?memberId=$script:memberId" -Method Get -Headers $adminHeaders
    $notifications = $null
    if ($rawRes.value -ne $null) {
        $notifications = $rawRes.value
    } else {
        $notifications = $rawRes
    }
    $firstNotifId = @($notifications)[0].notificationID

    $readRes = Invoke-RestMethod -Uri "$baseUrl/notifications/$firstNotifId/read" -Method Put -Headers $adminHeaders

    # Verify status
    $verifyRaw = Invoke-RestMethod -Uri "$baseUrl/notifications?memberId=$script:memberId" -Method Get -Headers $adminHeaders
    $verifyNotifs = $null
    if ($verifyRaw.value -ne $null) {
        $verifyNotifs = $verifyRaw.value
    } else {
        $verifyNotifs = $verifyRaw
    }
    $verifyNotif = @($verifyNotifs) | Where-Object { $_.notificationID -eq $firstNotifId }

    $verifyNotif.status -eq "Read"
}

# Stop API
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Notifications E2E Summary                   " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
