Write-Host "=== TEST 1: Recall Table T2 ===" -ForegroundColor Cyan
$t2 = Invoke-RestMethod -Uri "http://localhost:5000/api/tables/T2/order"
Write-Host "Table: $($t2.tableNumber), Covers: $($t2.coversCount), Total: $($t2.totalTtcAmount) EUR" -ForegroundColor Green
foreach ($line in $t2.lines) {
    Write-Host " - $($line.quantity)x $($line.productName) ($($line.unitPrice) EUR) [Dispatched: $($line.isDispatched)]" -ForegroundColor Yellow
}

Write-Host "`n=== TEST 2: Recall Table T6 ===" -ForegroundColor Cyan
$t6 = Invoke-RestMethod -Uri "http://localhost:5000/api/tables/T6/order"
Write-Host "Table: $($t6.tableNumber), Covers: $($t6.coversCount), Total: $($t6.totalTtcAmount) EUR" -ForegroundColor Green
foreach ($line in $t6.lines) {
    Write-Host " - $($line.quantity)x $($line.productName) ($($line.unitPrice) EUR) [Dispatched: $($line.isDispatched)]" -ForegroundColor Yellow
}

Write-Host "`n=== TEST 3: Add 2 Tiramisus to Table T2 ===" -ForegroundColor Cyan
$body = @{
    items = @(
        @{
            productId = [guid]::NewGuid().ToString()
            productName = "Tiramisu Spéculos Maison"
            quantity = 2
            unitPrice = 7.50
            taxRatePercent = 10.0
            preparationStationId = "DESSERT"
            modifiers = @("Supplément Cacao")
        }
    )
} | ConvertTo-Json -Depth 5

$updatedT2 = Invoke-RestMethod -Uri "http://localhost:5000/api/tables/T2/items" -Method Post -Body $body -ContentType "application/json"
Write-Host "New Total Table T2: $($updatedT2.totalTtcAmount) EUR" -ForegroundColor Green
foreach ($line in $updatedT2.lines) {
    Write-Host " - $($line.quantity)x $($line.productName) [Dispatched: $($line.isDispatched)]" -ForegroundColor Yellow
}

Write-Host "`n=== TEST 4: Dispatch New Items to Kitchen ===" -ForegroundColor Cyan
$dispatch = Invoke-RestMethod -Uri "http://localhost:5000/api/tables/T2/dispatch" -Method Post
Write-Host "Dispatch Status: $($dispatch.success)" -ForegroundColor Green

$afterDispatch = Invoke-RestMethod -Uri "http://localhost:5000/api/tables/T2/order"
foreach ($line in $afterDispatch.lines) {
    Write-Host " - $($line.quantity)x $($line.productName) [Dispatched: $($line.isDispatched)]" -ForegroundColor Yellow
}

Write-Host "`n=== TEST 5: Verify KDS Queue ===" -ForegroundColor Cyan
$tickets = Invoke-RestMethod -Uri "http://localhost:5000/api/kds/tickets"
Write-Host "Total tickets in KDS: $($tickets.Count)" -ForegroundColor Green
foreach ($t in $tickets) {
    Write-Host "Ticket: Table $($t.tableNumber) (Station: $($t.stationId), Status: $($t.status))" -ForegroundColor Magenta
}
