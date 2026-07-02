# GymTrackPro Admin Seeder Script
$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "Starting API to seed administrator..." -ForegroundColor Yellow
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
    Start-Sleep -Seconds 2
}

if (-not $apiReady) {
    Write-Host "API failed to start." -ForegroundColor Red
    exit 1
}

# Register admin if not exists
try {
    $adminReg = ConvertTo-Json @{
        Username = "admin"
        Email = "admin@gymtrack.pro"
        Password = "SecurePassword@123"
        FirstName = "System"
        LastName = "Administrator"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/auth/register" -Method Post -Body $adminReg -Headers $headers
    Write-Host "Admin registered." -ForegroundColor Green
} catch {
    Write-Host "Admin might already exist: $_" -ForegroundColor Yellow
}

# Update role & verification state
Write-Host "Promoting admin privileges in database..." -ForegroundColor Yellow
$updateQuery = "SET QUOTED_IDENTIFIER ON; UPDATE Users SET EmailVerified=1, Role=0, IsActive=1 WHERE Username='admin';"
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB -Q "$updateQuery" | Out-Null
Write-Host "Admin user promoted successfully." -ForegroundColor Green

Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force
Write-Host "Seeding complete!" -ForegroundColor Green
