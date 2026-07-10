# GymTrackPro Rate Limiting & Security Stress Test
# Verifies that the rate limiter successfully intercepts and blocks excessive requests.

$baseUrl = "http://localhost:5221/api/v1"
$syncUrl = "$baseUrl/auth/sync-user"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Rate Limiter Stress Tester     " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Check if API is running
Write-Host "Checking if GymTrackPro API is running..." -ForegroundColor Yellow
$apiReady = $false
try {
    $ping = Invoke-WebRequest -Uri "http://localhost:5221/openapi/v1.json" -Method Get -UseBasicParsing -ErrorAction SilentlyContinue
    if ($ping.StatusCode -eq 200) {
        $apiReady = $true
    }
} catch {
    # Ignore
}

$apiProcess = $null
if (-not $apiReady) {
    Write-Host "API is not running. Starting API..." -ForegroundColor Yellow
    $apiProcess = Start-Process dotnet -ArgumentList "run --project src/GymTrackPro.API --launch-profile http" -WorkingDirectory $PWD -PassThru -WindowStyle Hidden
    
    # Wait for API to respond
    for ($i = 1; $i -le 15; $i++) {
        try {
            $ping = Invoke-WebRequest -Uri "http://localhost:5221/openapi/v1.json" -Method Get -UseBasicParsing -ErrorAction Stop
            if ($ping.StatusCode -eq 200) {
                $apiReady = $true
                break;
            }
        } catch {
            # Ignore and retry
        }
        Write-Host "Waiting for API to start (attempt $i/15)..."
        Start-Sleep -Seconds 2
    }
    
    if (-not $apiReady) {
        Write-Host "Error: API failed to start." -ForegroundColor Red
        exit 1
    }
}

Write-Host "API is active. Waiting 15 seconds to ensure rate limit window is clear..." -ForegroundColor Yellow
Start-Sleep -Seconds 15

Write-Host "Initiating rate limit stress test (Limit: 5 requests/min)..." -ForegroundColor Green

$successCount = 0
$rateLimitedCount = 0

for ($i = 1; $i -le 8; $i++) {
    Write-Host "Sending Request #$i to /auth/sync-user..." -NoNewline
    try {
        $res = Invoke-WebRequest -Uri $syncUrl -Method Post -UseBasicParsing -ErrorAction Stop
        Write-Host " [Unexpected OK]" -ForegroundColor Yellow
    } catch {
        # Cross-version status parser (handles WebException and HttpResponseException)
        $status = 0
        if ($_.Exception.Response) {
            try {
                $status = [int]$_.Exception.Response.StatusCode
            } catch { }
        }
        if ($status -eq 0 -and $_.Exception.Message -match "\((\d{3})\)") {
            $status = [int]$Matches[1]
        }
        
        if ($status -eq 401) {
            Write-Host " [Passed Limiter -> Returned 401 Unauthorized as expected]" -ForegroundColor Green
            $successCount++
        } elseif ($status -eq 429 -or $status -eq 503) {
            Write-Host " [BLOCKED -> Returned $status Rate Limiting Response as expected]" -ForegroundColor Red
            $rateLimitedCount++
        } else {
            Write-Host " [Unexpected Status: $status (Msg: $($_.Exception.Message))]" -ForegroundColor Yellow
        }
    }
    # Tight delay to run requests in the same rate window
    Start-Sleep -Milliseconds 200
}

Write-Host "---------------------------------------------"
Write-Host "Stress Test Results:" -ForegroundColor Yellow
Write-Host " -> Allowed requests before limit: $successCount (Expected: 5)" -ForegroundColor Green
Write-Host " -> Blocked requests (429/503): $rateLimitedCount (Expected: 3)" -ForegroundColor Red

# Clean up API process if we started it
if ($apiProcess -ne $null) {
    Write-Host "Stopping background API process..." -ForegroundColor Yellow
    Stop-Process -Id $apiProcess.Id -Force
}

Write-Host "=============================================" -ForegroundColor Cyan
if ($successCount -eq 5 -and $rateLimitedCount -gt 0) {
    Write-Host "  TEST PASSED: Rate limiting is highly secure! " -ForegroundColor Green
    exit 0
} else {
    Write-Host "  TEST FAILED: Rate limit threshold not enforced correctly. " -ForegroundColor Red
    exit 1
}
