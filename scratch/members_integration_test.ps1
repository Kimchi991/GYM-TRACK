# GymTrackPro Members E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Members E2E Integration Test   " -ForegroundColor Cyan
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

# --- Case 1: Create Member ---
$member1Id = 0
$member1QR = ""
Assert-Test "Create Member - Valid Payload" {
    $body = ConvertTo-Json @{
        FirstName = "Alice"
        LastName = "Smith"
        Gender = "Female"
        BirthDate = "1995-05-15T00:00:00Z"
        PhoneNumber = "+1234567890"
        Email = "alice.smith@example.com"
        Address = "123 Main St, Springfield"
        EmergencyContact = "John Smith: +1987654321"
        ProfilePictureBase64 = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA="
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $body -Headers $recepHeaders
    $global:member1Id = $res.data.memberID
    $global:member1QR = $res.data.qrCode

    $res.success -eq $true -and $res.data.firstName -eq "Alice" -and $res.data.qrCode -like "GTP-*" -and $res.data.profilePicture -like "/uploads/profiles/*.jpg"
}

# --- Case 2: Validation of Duplicates ---
Assert-Test "Create Member - Reject Duplicate Phone" {
    $body = ConvertTo-Json @{
        FirstName = "Bob"
        LastName = "Jones"
        Gender = "Male"
        BirthDate = "1990-10-10T00:00:00Z"
        PhoneNumber = "+1234567890" # Duplicate
        Email = "bob.jones@example.com"
        EmergencyContact = "Mary Jones: +1112223333"
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $body -Headers $recepHeaders
    } catch {
        $rejected = $_.Exception.Response.StatusCode -eq "BadRequest" -or $_.Exception.Response.StatusCode -eq "InternalServerError"
    }
    $rejected
}

Assert-Test "Create Member - Reject Duplicate Email" {
    $body = ConvertTo-Json @{
        FirstName = "Bob"
        LastName = "Jones"
        Gender = "Male"
        BirthDate = "1990-10-10T00:00:00Z"
        PhoneNumber = "+1999999999"
        Email = "alice.smith@example.com" # Duplicate
        EmergencyContact = "Mary Jones: +1112223333"
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $body -Headers $recepHeaders
    } catch {
        $rejected = $_.Exception.Response.StatusCode -eq "BadRequest" -or $_.Exception.Response.StatusCode -eq "InternalServerError"
    }
    $rejected
}

# --- Create Additional Members for Searching/Paging ---
$body2 = ConvertTo-Json @{
    FirstName = "Bob"
    LastName = "Johnson"
    Gender = "Male"
    BirthDate = "1988-08-08T00:00:00Z"
    PhoneNumber = "+1555123456"
    Email = "bob.johnson@example.com"
    EmergencyContact = "Sarah Johnson: +1555987654"
}
$member2 = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $body2 -Headers $recepHeaders

$body3 = ConvertTo-Json @{
    FirstName = "Charlie"
    LastName = "Smith"
    Gender = "Male"
    BirthDate = "2000-01-01T00:00:00Z"
    PhoneNumber = "+1777000111"
    Email = "charlie.smith@example.com"
    EmergencyContact = "Jane Smith: +1777222333"
}
$member3 = Invoke-RestMethod -Uri "$baseUrl/members" -Method Post -Body $body3 -Headers $recepHeaders
$member3Id = $member3.data.memberID

# --- Case 3: Get By ID and QR Code ---
Assert-Test "Get Member by ID" {
    $res = Invoke-RestMethod -Uri "$baseUrl/members/$member1Id" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $res.data.firstName -eq "Alice"
}

Assert-Test "Get Member by QR Code" {
    $res = Invoke-RestMethod -Uri "$baseUrl/members/qr/$member1QR" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $res.data.memberID -eq $member1Id
}

# --- Case 4: Search, Filtering & Pagination ---
Assert-Test "Search Member by Name" {
    $res = Invoke-RestMethod -Uri "$baseUrl/members/search?search=Smith" -Method Get -Headers $recepHeaders
    # Alice Smith and Charlie Smith
    $res.success -eq $true -and $res.data.totalCount -eq 2
}

Assert-Test "Search Member by Phone" {
    $res = Invoke-RestMethod -Uri "$baseUrl/members/search?search=555123" -Method Get -Headers $recepHeaders
    # Bob Johnson
    $res.success -eq $true -and $res.data.items[0].firstName -eq "Bob"
}

Assert-Test "Filter Member by Status" {
    # Set Bob Johnson to Inactive
    $updateBob = ConvertTo-Json @{
        FirstName = "Bob"
        LastName = "Johnson"
        Gender = "Male"
        BirthDate = "1988-08-08T00:00:00Z"
        PhoneNumber = "+1555123456"
        Email = "bob.johnson@example.com"
        EmergencyContact = "Sarah Johnson: +1555987654"
        Status = "Inactive"
    }
    $updateRes = Invoke-RestMethod -Uri "$baseUrl/members/$($member2.data.memberID)" -Method Put -Body $updateBob -Headers $recepHeaders
    
    $res = Invoke-RestMethod -Uri "$baseUrl/members/search?status=Inactive" -Method Get -Headers $recepHeaders
    $res.success -eq $true -and $res.data.totalCount -eq 1 -and $res.data.items[0].lastName -eq "Johnson"
}

Assert-Test "Pagination Test" {
    $res = Invoke-RestMethod -Uri "$baseUrl/members/search?page=1&pageSize=2" -Method Get -Headers $recepHeaders
    # Total count is 3, page size is 2, so totalPages should be 2
    $res.success -eq $true -and $res.data.items.Count -eq 2 -and $res.data.totalPages -eq 2
}

# --- Case 5: Update Member & Uniqueness Checks ---
Assert-Test "Update Member Profile" {
    $body = ConvertTo-Json @{
        FirstName = "Alice"
        LastName = "Smith-Jones"
        Gender = "Female"
        BirthDate = "1995-05-15T00:00:00Z"
        PhoneNumber = "+1234567890"
        Email = "alice.smith.new@example.com"
        Address = "456 Oak Ave, Springfield"
        EmergencyContact = "John Smith: +1987654321"
        Status = "Active"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/members/$member1Id" -Method Put -Body $body -Headers $recepHeaders
    $res.success -eq $true -and $res.data.lastName -eq "Smith-Jones" -and $res.data.email -eq "alice.smith.new@example.com"
}

Assert-Test "Update Member - Reject Duplicate Phone" {
    $body = ConvertTo-Json @{
        FirstName = "Alice"
        LastName = "Smith-Jones"
        Gender = "Female"
        BirthDate = "1995-05-15T00:00:00Z"
        PhoneNumber = "+1555123456" # Bob's phone number
        Email = "alice.smith.new@example.com"
        EmergencyContact = "John Smith: +1987654321"
        Status = "Active"
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/members/$member1Id" -Method Put -Body $body -Headers $recepHeaders
    } catch {
        $rejected = $_.Exception.Response.StatusCode -eq "BadRequest" -or $_.Exception.Response.StatusCode -eq "InternalServerError"
    }
    $rejected
}

# --- Case 6: Authorization & Soft Delete ---
Assert-Test "Soft Delete Member - Block Receptionist" {
    $blocked = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/members/$member3Id" -Method Delete -Headers $recepHeaders
    } catch {
        $blocked = $_.Exception.Response.StatusCode -eq "Forbidden"
    }
    $blocked
}

Assert-Test "Soft Delete Member - Allow Admin" {
    $res = Invoke-RestMethod -Uri "$baseUrl/members/$member3Id" -Method Delete -Headers $adminHeaders
    $res.success -eq $true
}

Assert-Test "Soft Deleted Member - Retrieve Returns 404" {
    $notFound = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/members/$member3Id" -Method Get -Headers $recepHeaders
    } catch {
        $notFound = $_.Exception.Response.StatusCode -eq "NotFound"
    }
    $notFound
}

# --- Case 7: Audit Logging Check ---
Assert-Test "Audit Logging - Verify Logs Written" {
    $logQuery = "SET NOCOUNT ON; SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('Member Created', 'Member Updated', 'Member Deleted');"
    $logCount = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$logQuery").Trim()
    [int]$logCount -ge 3
}

# Stop the API process
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Members Integration Test Results Summary   " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
