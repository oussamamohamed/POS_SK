# Phase 5 verification script: Configurable POS Management & Administration
param (
    [string]$Configuration = "Release"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Verifying Phase 5: Configurable POS Management & Administration" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Clean build solution
Write-Host "`n[1/3] Building solution..." -ForegroundColor Yellow
dotnet build RestaurantPos.slnx -c $Configuration /p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution build failed!"
    exit 1
}

# 2. Run all test projects
Write-Host "`n[2/3] Running domain, infrastructure, and client test suites..." -ForegroundColor Yellow
dotnet test tests/RestaurantPos.Domain.Tests/RestaurantPos.Domain.Tests.csproj -c $Configuration --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Domain test suite failed!"
    exit 1
}

dotnet test tests/RestaurantPos.Infrastructure.Tests/RestaurantPos.Infrastructure.Tests.csproj -c $Configuration --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Infrastructure test suite failed!"
    exit 1
}

dotnet test tests/RestaurantPos.Client.Maui.Tests/RestaurantPos.Client.Maui.Tests.csproj -c $Configuration --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Client MAUI test suite failed!"
    exit 1
}

Write-Host "`n[3/3] Configuration & Administration module successfully verified with 0 errors and 0 warnings!" -ForegroundColor Green
exit 0
