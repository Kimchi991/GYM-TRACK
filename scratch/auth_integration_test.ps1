# GymTrackPro Authentication E2E Integration Test
# Assumes API runs on http://localhost:5221

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Auth E2E Integration Test      " -ForegroundColor Cyan
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
        # Catching unauthorized (401) is fine as it means the API is up!
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
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response Body: $responseBody" -ForegroundColor DarkYellow
            $testResults.Add("[FAIL] $name (Body: $responseBody)")
        } else {
            Write-Host "Exception: $_" -ForegroundColor Red
            $testResults.Add("[FAIL] $name (Exception: $_)")
        }
    }
}

# --- Case 1: Register User ---
Assert-Test "Register Admin User" {
    $registerBody = ConvertTo-Json @{
        Username = "admin_user"
        Email = "admin@gymtrack.pro"
        Password = "SecurePassword@123"
        FirstName = "Admin"
        LastName = "User"
    }
    $response = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $registerBody -Headers $headers
    $response.success -eq $true -and $response.data.username -eq "admin_user" -and $response.data.emailVerified -eq $false
}

# --- Case 2: Duplicate Register Check ---
Assert-Test "Duplicate Username / Email Check" {
    $registerBody = ConvertTo-Json @{
        Username = "admin_user"
        Email = "admin@gymtrack.pro"
        Password = "SecurePassword@123"
        FirstName = "Admin"
        LastName = "User"
    }
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $registerBody -Headers $headers
        $false
    } catch {
        $_.Exception.Response.StatusCode -eq "BadRequest"
    }
}

# --- Case 3: Login before Email Verification ---
Assert-Test "Login without Email Verification Block" {
    $loginBody = ConvertTo-Json @{
        Username = "admin_user"
        Password = "SecurePassword@123"
    }
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body $loginBody -Headers $headers
        $false
    } catch {
        # Custom message should state email not verified
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $errBody.message -like "*not verified*"
    }
}

# --- Case 4: Email Verification ---
Assert-Test "Verify Email using Token" {
    # Query token
    $tokenQuery = "SET NOCOUNT ON; SELECT VerificationToken FROM Users WHERE Username='admin_user';"
    $token = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$tokenQuery").Trim()
    
    $verifyBody = ConvertTo-Json @{
        Email = "admin@gymtrack.pro"
        Token = $token
    }
    $response = Invoke-RestMethod -Uri "$baseUrl/auth/verify-email" -Method Post -Body $verifyBody -Headers $headers
    
    # Elevate role to Administrator for testing
    $updateQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; UPDATE Users SET Role=0 WHERE Username='admin_user';"
    docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$updateQuery" | Out-Null
    
    $response.success -eq $true -and $response.message -like "*verified successfully*"
}

# --- Case 5: Successful Login & JWT Token Retrieval ---
$adminToken = ""
Assert-Test "Login with Verified Credentials" {
    $loginBody = ConvertTo-Json @{
        Username = "admin_user"
        Password = "SecurePassword@123"
    }
    $response = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body $loginBody -Headers $headers
    $global:adminToken = $response.data.token
    $response.success -eq $true -and $response.data.token -ne $null -and $response.data.role -eq "Administrator"
}

# --- Case 6: Inactive User Block ---
Assert-Test "Inactive User Login Block" {
    # Set to inactive
    $updateQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; UPDATE Users SET IsActive=0 WHERE Username='admin_user';"
    docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$updateQuery" | Out-Null
    
    $loginBody = ConvertTo-Json @{
        Username = "admin_user"
        Password = "SecurePassword@123"
    }
    $blocked = $false
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body $loginBody -Headers $headers
    } catch {
        $errResponse = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errResponse)
        $errBody = $reader.ReadToEnd() | ConvertFrom-Json
        $blocked = $errBody.message -like "*inactive*"
    }
    
    # Re-enable
    $updateQuery = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; UPDATE Users SET IsActive=1 WHERE Username='admin_user';"
    docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$updateQuery" | Out-Null
    
    $blocked
}

# --- Case 7: Forgot & Reset Password ---
Assert-Test "Forgot and Reset Password Flow" {
    # Request reset
    $forgotBody = ConvertTo-Json "admin@gymtrack.pro"
    $response = Invoke-RestMethod -Uri "$baseUrl/auth/forgot-password" -Method Post -Body $forgotBody -Headers $headers
    
    # Query Reset Token
    $tokenQuery = "SET NOCOUNT ON; SELECT ResetToken FROM Users WHERE Username='admin_user';"
    $resetToken = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$tokenQuery").Trim()
    
    $resetBody = ConvertTo-Json @{
        Email = "admin@gymtrack.pro"
        Token = $resetToken
        NewPassword = "NewSecurePassword@999"
    }
    $resetResponse = Invoke-RestMethod -Uri "$baseUrl/auth/reset-password" -Method Post -Body $resetBody -Headers $headers
    
    # Try logging in with new password
    $loginBody = ConvertTo-Json @{
        Username = "admin_user"
        Password = "NewSecurePassword@999"
    }
    $loginResponse = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body $loginBody -Headers $headers
    $global:adminToken = $loginResponse.data.token
    
    $resetResponse.success -eq $true -and $loginResponse.success -eq $true
}

# --- Case 8: Role Authorization (Administrator vs Receptionist) ---
Assert-Test "Role Authorization Restrictions" {
    # 1. Register and verify receptionist
    $receptionBody = ConvertTo-Json @{
        Username = "recep_user"
        Email = "reception@gymtrack.pro"
        Password = "SecurePassword@123"
        FirstName = "Recep"
        LastName = "User"
    }
    $regResponse = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $receptionBody -Headers $headers
    
    $tokenQuery = "SET NOCOUNT ON; SELECT VerificationToken FROM Users WHERE Username='recep_user';"
    $recepVerifToken = (docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -W -h -1 -Q "$tokenQuery").Trim()
    
    $verifyBody = ConvertTo-Json @{
        Email = "reception@gymtrack.pro"
        Token = $recepVerifToken
    }
    Invoke-RestMethod -Uri "$baseUrl/auth/verify-email" -Method Post -Body $verifyBody -Headers $headers | Out-Null
    
    # Login receptionist
    $loginBody = ConvertTo-Json @{
        Username = "recep_user"
        Password = "SecurePassword@123"
    }
    $loginResponse = Invoke-RestMethod -Uri "$baseUrl/auth/login" -Method Post -Body $loginBody -Headers $headers
    $recepToken = $loginResponse.data.token
    
    # 2. Try creating a plan as Receptionist (should fail 403)
    $planBody = ConvertTo-Json @{
        PlanName = "Receptionist Plan Test"
        DurationDays = 30
        Price = 1000.00
    }
    $recepHeaders = @{
        "Content-Type" = "application/json"
        "Authorization" = "Bearer $recepToken"
    }
    $recepBlocked = $false
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $planBody -Headers $recepHeaders
    } catch {
        if ($_.Exception.Response) {
            Write-Host "Recep Exception StatusCode: $($_.Exception.Response.StatusCode)" -ForegroundColor DarkYellow
            $recepBlocked = $_.Exception.Response.StatusCode -eq "Forbidden"
        } else {
            Write-Host "Recep Exception: $_" -ForegroundColor Red
        }
    }
    
    # 3. Create plan as Admin (should succeed)
    $adminHeaders = @{
        "Content-Type" = "application/json"
        "Authorization" = "Bearer $global:adminToken"
    }
    $adminAllowed = $false
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/plans" -Method Post -Body $planBody -Headers $adminHeaders
        $adminAllowed = $response.success -eq $true
    } catch {
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            Write-Host "Admin Exception Body: $($reader.ReadToEnd())" -ForegroundColor DarkYellow
        } else {
            Write-Host "Admin Exception: $_" -ForegroundColor Red
        }
        $adminAllowed = $false
    }
    
    Write-Host "Debug: recepBlocked = $recepBlocked, adminAllowed = $adminAllowed" -ForegroundColor DarkYellow
    $recepBlocked -and $adminAllowed
}

# Cleanup: Stop the background API
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Auth Integration Test Results Summary      " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($test in $testResults) {
    if ($test -like "*PASS*") {
        Write-Host $test -ForegroundColor Green
    } else {
        Write-Host $test -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
