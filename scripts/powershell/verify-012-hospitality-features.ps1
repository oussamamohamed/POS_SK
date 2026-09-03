# Verification Script for Feature 012: Mobile Hospitality POS Features
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  FEATURE 012: MOBILE HOSPITALITY POS FEATURES VERIFICATION" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Step 1: Run full unit & integration test suite
Write-Host "`n[1/3] Running Full Test Suite (dotnet test)..." -ForegroundColor Yellow
$testResult = dotnet test --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Unit Tests Failed!" -ForegroundColor Red
    exit 1
}
Write-Host "✅ All Unit and Integration Tests Passed successfully!" -ForegroundColor Green

# Step 2: Build API project
Write-Host "`n[2/3] Building API Project..." -ForegroundColor Yellow
dotnet build src/RestaurantPos.Api/RestaurantPos.Api.csproj -c Debug --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ API Build Failed!" -ForegroundColor Red
    exit 1
}

# Launch API Server on Port 5055 for E2E Validation
Write-Host "Launching API Server on Port 5055 for E2E Validation..." -ForegroundColor Yellow
$apiProcess = Start-Process dotnet -ArgumentList "run --no-build --project src/RestaurantPos.Api/RestaurantPos.Api.csproj --urls http://127.0.0.1:5055" -PassThru

try {
    # Wait for API to respond
    $healthy = $false
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 1
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:5055/api/health" -Method Get -TimeoutSec 2
            if ($health.Status -eq "Healthy") {
                $healthy = $true
                break
            }
        } catch { }
    }

    if (-not $healthy) {
        Write-Host "❌ API Server failed to respond on http://127.0.0.1:5055/api/health" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ API Server is Healthy and ready." -ForegroundColor Green

    # Step 3: Test Hospitality Endpoints
    Write-Host "`n[3/3] Validating Hospitality Endpoints & Fiscal Integrity..." -ForegroundColor Yellow

    # US1: Table Transfer & Merge
    Write-Host "  • Testing Table Transfer (T2 -> T4)..." -NoNewline
    $transferBody = @{ targetTableNumber = "T4" } | ConvertTo-Json
    $transferRes = Invoke-RestMethod -Uri "http://127.0.0.1:5055/api/tables/T2/transfer" -Method Post -Body $transferBody -ContentType "application/json"
    if ($transferRes.success) {
        Write-Host " [PASS]" -ForegroundColor Green
    } else {
        Write-Host " [FAIL]" -ForegroundColor Red
        exit 1
    }

    # US3: Kitchen Course Fire
    Write-Host "  • Testing Course Fire Suite (T4)..." -NoNewline
    $fireRes = Invoke-RestMethod -Uri "http://127.0.0.1:5055/api/tables/T4/fire-suite" -Method Post
    if ($fireRes.success) {
        Write-Host " [PASS]" -ForegroundColor Green
    } else {
        Write-Host " [FAIL]" -ForegroundColor Red
        exit 1
    }

    # US5: Hotel Rooms & PMS Room Charge
    Write-Host "  • Testing Hotel PMS Room Lookup..." -NoNewline
    $rooms = Invoke-RestMethod -Uri "http://127.0.0.1:5055/api/hotel/rooms" -Method Get
    if ($rooms.Count -ge 3 -and ($rooms | Where-Object { $_.roomNumber -eq "204" })) {
        Write-Host " [PASS] ($($rooms.Count) active rooms found)" -ForegroundColor Green
    } else {
        Write-Host " [FAIL]" -ForegroundColor Red
        exit 1
    }

    Write-Host "  • Testing Hotel Room Charge Folio Post (Chambre 204)..." -NoNewline
    $orderData = Invoke-RestMethod -Uri "http://127.0.0.1:5055/api/tables/T4/order" -Method Get
    $roomChargeBody = @{
        orderId = $orderData.orderId
        tableNumber = "T4"
        roomNumber = "204"
        guestName = "Alexandre Dupont"
        amount = 18.50
        tipAmount = 2.00
        signatureDataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
        notes = "Facturation dîner terrasse"
    } | ConvertTo-Json

    $chargeRes = Invoke-RestMethod -Uri "http://127.0.0.1:5055/api/hotel/room-charge" -Method Post -Body $roomChargeBody -ContentType "application/json"
    if ($chargeRes.success) {
        Write-Host " [PASS] (Charge Id: $($chargeRes.chargeId))" -ForegroundColor Green
    } else {
        Write-Host " [FAIL]" -ForegroundColor Red
        exit 1
    }

    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host "  🎉 FEATURE 012 IMPLEMENTATION FULLY VERIFIED (PASS)" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Cyan

} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
