# GymTrackPro Trainer Enablement & Workout Routines E2E Integration Test
# Assumes API runs on http://localhost:5221
# Connects directly to the SQL Server database via ADO.NET using appsettings.Development.json connection string.

$baseUrl = "http://localhost:5221/api/v1"
$headers = @{ "Content-Type" = "application/json" }

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GymTrackPro Trainer & Routines E2E Test    " -ForegroundColor Cyan
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

# 0. Clean DB tables for test run
Write-Host "Cleaning database records..." -ForegroundColor Yellow
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

# --- Setup Users & Members ---
Write-Host "Setting up test users, trainers, and members..." -ForegroundColor Yellow

# Insert Receptionist (Role = 1)
$insertRecep = @"
INSERT INTO Users (Username, Email, NormalizedEmail, FirebaseUid, FirstName, LastName, Role, EmailVerified, IsActive, CreatedAt, UpdatedAt)
VALUES ('recep_user', 'recep@gymtrack.pro', 'recep@gymtrack.pro', 'recep_firebase_uid', 'Receptionist', 'User', 1, 1, 1, GETUTCDATE(), GETUTCDATE());
"@
Execute-Sql $insertRecep | Out-Null
$recepHeaders = @{
    "Content-Type" = "application/json"
    "X-Test-User-Uid" = "recep_firebase_uid"
    "X-Test-User-Email" = "recep@gymtrack.pro"
}

# Insert Trainer (Role = 3)
$insertTrainer = @"
INSERT INTO Users (Username, Email, NormalizedEmail, FirebaseUid, FirstName, LastName, Role, EmailVerified, IsActive, CreatedAt, UpdatedAt)
VALUES ('trainer_user', 'trainer@gymtrack.pro', 'trainer@gymtrack.pro', 'trainer_firebase_uid', 'John', 'Coach', 3, 1, 1, GETUTCDATE(), GETUTCDATE());
"@
Execute-Sql $insertTrainer | Out-Null
$trainerUserId = [int](Execute-SqlScalar "SELECT UserID FROM Users WHERE Username = 'trainer_user';")

$trainerHeaders = @{
    "Content-Type" = "application/json"
    "X-Test-User-Uid" = "trainer_firebase_uid"
    "X-Test-User-Email" = "trainer@gymtrack.pro"
}

# Insert Member
$insertMember = @"
INSERT INTO Members (FirstName, LastName, Gender, BirthDate, PhoneNumber, Email, EmergencyContact, QRCode, Status, DateRegistered, LastModified, IsDeleted)
VALUES ('Bob', 'Client', 'Male', '1995-05-15', '09179998888', 'bob_goer@gymtrack.pro', 'Emergency Mom', 'QR-BOB-CLIENT', 'Active', GETUTCDATE(), GETUTCDATE(), 0);
"@
Execute-Sql $insertMember | Out-Null
$memberId = [int](Execute-SqlScalar "SELECT MemberID FROM Members WHERE QRCode = 'QR-BOB-CLIENT';")

# Insert MemberProjectionVersion
$insertVersion = @"
INSERT INTO MemberProjectionVersions (MemberID, Version)
VALUES ($memberId, 1);
"@
Execute-Sql $insertVersion | Out-Null

# Link Member to a User (Role = 2)
$insertMemberUser = @"
INSERT INTO Users (Username, Email, NormalizedEmail, FirebaseUid, FirstName, LastName, Role, EmailVerified, IsActive, CreatedAt, UpdatedAt, MemberID)
VALUES ('bob_goer', 'bob_goer@gymtrack.pro', 'bob_goer@gymtrack.pro', 'bob_goer_firebase_uid', 'Bob', 'Client', 2, 1, 1, GETUTCDATE(), GETUTCDATE(), $memberId);
"@
Execute-Sql $insertMemberUser | Out-Null

$insertSetting = @"
IF NOT EXISTS (SELECT 1 FROM SystemSettings WHERE SettingKey = 'GymTimeZone')
BEGIN
    INSERT INTO SystemSettings (SettingKey, SettingValue, GroupName, Description, LastModified)
    VALUES ('GymTimeZone', 'Singapore Standard Time', 'General', 'Gym Timezone', GETUTCDATE());
END
"@
Execute-Sql $insertSetting | Out-Null

$memberHeaders = @{
    "Content-Type" = "application/json"
    "X-Test-User-Uid" = "bob_goer_firebase_uid"
    "X-Test-User-Email" = "bob_goer@gymtrack.pro"
}

Write-Host "Setup complete. Running trainer enablement test cases..." -ForegroundColor Green

# --- Case 1: Assign Client to Trainer (Receptionist) ---
Assert-Test "Assign Client to Trainer via Receptionist" {
    $body = ConvertTo-Json @{
        trainerUserID = $trainerUserId
        memberID = $memberId
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/trainers/assign" -Method Post -Body $body -Headers $recepHeaders
    
    # Check DB
    $tcCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM TrainerClients WHERE TrainerUserID = $trainerUserId AND MemberID = $memberId AND IsActive = 1;")
    
    $res.success -eq $true -and $tcCount -eq 1
}

# --- Case 2: Reject Invalid Assignment ---
Assert-Test "Reject Assigning Client to Non-Trainer User" {
    $body = ConvertTo-Json @{
        trainerUserID = 9999
        memberID = $memberId
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/trainers/assign" -Method Post -Body $body -Headers $recepHeaders
    } catch {
        if ($_.Exception.Response.StatusCode -eq "BadRequest") {
            $rejected = $true
        }
    }
    $rejected
}

# --- Case 3: Post Workout Routine (Trainer) ---
Assert-Test "Create Workout Routine via Trainer" {
    $exercises = @(
        @{ name = "Bench Press"; sets = 4; reps = 10; weight = 60.0 },
        @{ name = "Squat"; sets = 4; reps = 8; weight = 80.0 }
    )
    $body = ConvertTo-Json @{
        memberID = $memberId
        routineName = "Strength Hypertrophy"
        exercisesJson = (ConvertTo-Json $exercises -Compress)
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/trainers/routines" -Method Post -Body $body -Headers $trainerHeaders
    
    # Check DB
    $routineCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM WorkoutRoutines WHERE TrainerUserID = $trainerUserId AND MemberID = $memberId AND RoutineName = 'Strength Hypertrophy';")
    
    $res.success -eq $true -and $routineCount -eq 1 -and $res.data.routineName -eq "Strength Hypertrophy"
}

# --- Case 4: Get Trainer's Assigned Clients ---
Assert-Test "Get Trainer Assigned Clients List" {
    $res = Invoke-RestMethod -Uri "$baseUrl/trainers/clients" -Method Get -Headers $trainerHeaders
    $res.success -eq $true -and $res.data.Count -eq 1 -and $res.data[0].fullName -eq "Bob Client"
}

# --- Case 5: Get Member Workout Routine (Member) ---
Assert-Test "Get Member Self-Service Assigned Workout Routine" {
    $res = Invoke-RestMethod -Uri "$baseUrl/me/workout-routine" -Method Get -Headers $memberHeaders
    
    # Decode exercises count
    $exercises = $res.data.exercisesJson | ConvertFrom-Json
    
    $res.success -eq $true -and $res.data.routineName -eq "Strength Hypertrophy" -and $exercises.Count -eq 2
}

# --- Case 6: Reject Duplicate Pending Member Application ---
Assert-Test "Reject Duplicate Pending Registration Application" {
    $appBody = ConvertTo-Json @{
        fullName = "Duplicate Applicant"
        contactNumber = "09171112222"
        emailAddress = "dup_pending@gymtrack.pro"
        isOneDayPass = $true
        paymentMethod = "GCash"
        paymentReferenceNumber = "REF-DUP-111"
    }
    # Submit first time -> Success
    $firstRes = Invoke-RestMethod -Uri "$baseUrl/applications" -Method Post -Body $appBody -Headers @{"Content-Type"="application/json"}
    
    # Submit second time -> Rejection (BadRequest)
    $secondRejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/applications" -Method Post -Body $appBody -Headers @{"Content-Type"="application/json"}
    } catch {
        if ($_.Exception.Response.StatusCode -eq "BadRequest") {
            $secondRejected = $true
        }
    }
    $firstRes.success -eq $true -and $secondRejected
}

# --- Case 7: Reject Invalid Workout Routine JSON ---
Assert-Test "Reject Invalid Exercises JSON Syntax" {
    $invalidBody = ConvertTo-Json @{
        memberID = $memberId
        routineName = "Broken Format"
        exercisesJson = "this is not json"
    }
    $rejected = $false
    try {
        Invoke-RestMethod -Uri "$baseUrl/trainers/routines" -Method Post -Body $invalidBody -Headers $trainerHeaders
    } catch {
        if ($_.Exception.Response.StatusCode -eq "BadRequest") {
            $rejected = $true
        }
    }
    $rejected
}

# --- Case 8: Member Submits Completed Workout Log ---
Assert-Test "Member Submit Completed Workout Session Log" {
    $completedExercises = @(
        @{ name = "Bench Press"; sets = 4; reps = 10; weight = 60.0 },
        @{ name = "Squat"; sets = 4; reps = 8; weight = 80.0 }
    )
    $logBody = ConvertTo-Json @{
        routineName = "Strength Hypertrophy"
        completedExercisesJson = (ConvertTo-Json $completedExercises -Compress)
        notes = "Felt great today!"
    }
    $res = Invoke-RestMethod -Uri "$baseUrl/me/workout-logs" -Method Post -Body $logBody -Headers $memberHeaders
    
    # Check DB
    $logCount = [int](Execute-SqlScalar "SELECT COUNT(*) FROM WorkoutLogs WHERE MemberID = $memberId AND RoutineName = 'Strength Hypertrophy';")
    
    $res.success -eq $true -and $logCount -ge 1 -and $res.data.routineName -eq "Strength Hypertrophy"
}

# --- Case 9: Member & Trainer Fetch Workout Logs History ---
Assert-Test "Fetch Member & Trainer Workout Log History" {
    $memberLogs = Invoke-RestMethod -Uri "$baseUrl/me/workout-logs" -Method Get -Headers $memberHeaders
    $trainerLogs = Invoke-RestMethod -Uri "$baseUrl/trainers/clients/$memberId/logs" -Method Get -Headers $trainerHeaders
    
    $memberLogs.success -eq $true -and $memberLogs.data.Count -ge 1 -and $trainerLogs.success -eq $true -and $trainerLogs.data.Count -ge 1
}

# --- Case 10: Export Emergency Evacuation Roster Manifest ---
Assert-Test "Export Emergency Evacuation Manifest Roster" {
    $manifest = Invoke-RestMethod -Uri "$baseUrl/attendance/emergency-manifest" -Method Get -Headers $recepHeaders
    $manifest.success -eq $true -and $manifest.data.exportedAtUtc -ne $null
}

# --- Case 11: Member Home Live Occupancy Widget Data ---
Assert-Test "Member Home Live Occupancy Widget Data" {
    $goerDashboard = Invoke-RestMethod -Uri "$baseUrl/me/dashboard" -Method Get -Headers $memberHeaders
    $goerDashboard.success -eq $true -and $goerDashboard.data.maxCapacity -eq 50 -and $goerDashboard.data.occupancyStatus -ne $null
}

# Stop the API process
Write-Host "Stopping API..." -ForegroundColor Yellow
Stop-Process -Id $apiProcess.Id -Force

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Trainer & Routines Integration Test Summary " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
foreach ($r in $testResults) {
    if ($r -like "*PASS*") {
        Write-Host $r -ForegroundColor Green
    } else {
        Write-Host $r -ForegroundColor Red
    }
}
Write-Host "=============================================" -ForegroundColor Cyan
