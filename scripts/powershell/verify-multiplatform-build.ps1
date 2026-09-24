# Multi-platform build verification script
param (
    [string]$Configuration = "Release"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Verifying Multi-Platform Restaurant POS Solution Build" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Clean build solution
Write-Host "`n[1/3] Building solution projects..." -ForegroundColor Yellow
dotnet build RestaurantPos.slnx -c $Configuration /p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution build failed!"
    exit 1
}

# 2. Run all unit and integration test suites
Write-Host "`n[2/3] Running test suites..." -ForegroundColor Yellow
dotnet test tests/RestaurantPos.Client.Maui.Tests/RestaurantPos.Client.Maui.Tests.csproj -c $Configuration --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Test suite failed!"
    exit 1
}

Write-Host "`n[3/3] Build and test verification completed successfully with 0 errors and 0 warnings!" -ForegroundColor Green
exit 0
