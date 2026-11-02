param([string]$BaseUrl = 'http://localhost:5080')
$ErrorActionPreference = 'Stop'
$checks = 0
function Assert($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Request([string]$Method, [string]$Path, $Body = $null) {
    $arguments = @{ Uri = "$BaseUrl$Path"; Method = $Method; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = ConvertTo-Json -InputObject $Body -Compress
    }
    $response = Invoke-WebRequest @arguments
    $json = if ($response.Content) { $response.Content | ConvertFrom-Json } else { $null }
    return @{ Status = [int]$response.StatusCode; Json = $json; Headers = $response.Headers }
}

# Local integration suite: uses SQL*Plus only to check history and remove its own test row.
$credentialPath = Join-Path $PSScriptRoot '../database/connection.local.txt'
$password = ((Get-Content -LiteralPath $credentialPath) | Where-Object { $_ -like 'Password: *' }).Substring(10)
function Sql([string]$Statement) {
    $sql = @"
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
CONNECT ATC_APP/"$password"@localhost:1521/FREEPDB1
SET HEADING OFF FEEDBACK OFF PAGESIZE 0 VERIFY OFF ECHO OFF
$Statement
EXIT
"@
    $output = $sql | sqlplus -s /nolog
    if ($LASTEXITCODE -ne 0) { throw "SQL verification failed: $output" }
    return ($output -join "`n").Trim()
}

$createdId = $null
try {
    $departments = Request GET '/api/departments'
    $products = Request GET '/api/products'
    Assert ($departments.Status -eq 200 -and $departments.Json.Count -gt 0) 'Department choices failed'
    Assert ($products.Status -eq 200 -and $products.Json.Count -gt 0) 'Product choices failed'
    $all = Request GET '/api/orders?pageSize=200'
    Assert ($all.Status -eq 200 -and $all.Json.totalCount -gt 0) 'Order list failed'
    Assert ((Request GET '/api/orders?dueTo=9999-12-31').Json.totalCount -eq $all.Json.totalCount) 'Maximum inclusive date failed'
    $first = $all.Json.items[0]
    $detail = Request GET "/api/orders/$($first.orderId)"
    Assert ($detail.Status -eq 200 -and $detail.Json.orderId -eq $first.orderId) 'Order detail failed'
    $filtered = Request GET "/api/orders?departmentId=$($first.departmentId)&productId=$($first.productId)&status=$([uri]::EscapeDataString($first.status))&dueFrom=$($first.dueDate)&dueTo=$($first.dueDate)&search=$($first.partNumber)"
    Assert ($filtered.Status -eq 200 -and $filtered.Json.totalCount -gt 0) 'Combined filters failed'
    Assert (@($filtered.Json.items | Where-Object { $_.departmentId -ne $first.departmentId -or $_.dueDate -ne $first.dueDate }).Count -eq 0) 'Filter returned unexpected rows'
    Assert ((Request GET '/api/orders?search=%27%20OR%201%3D1--').Json.totalCount -eq 0) 'Search was not parameterized'
    Assert ((Request GET '/api/orders?search=%25').Json.totalCount -eq 0) 'Literal wildcard escaping failed'
    Assert ((Request GET '/api/orders/999999999').Status -eq 404) 'Missing order should return 404'
    foreach ($path in @('/api/orders?page=0', '/api/orders?pageSize=201', '/api/orders?status=Bad', '/api/orders?dueFrom=2026-12-01&dueTo=2026-01-01', '/api/orders?dueFrom=invalid')) {
        Assert ((Request GET $path).Status -eq 400) "Expected validation error: $path"
    }
    $empty = Request GET '/api/orders?page=1000000'
    Assert ($empty.Status -eq 200 -and $empty.Json.items.Count -eq 0 -and $empty.Json.totalCount -eq $all.Json.totalCount) 'Empty page lost totals'
    $report = Request GET '/api/reports/order-exceptions?asOfDate=2026-11-02&pageSize=200'
    Assert ($report.Status -eq 200) 'Report failed'
    Assert (@($report.Json.items | Where-Object { -not $_.isOverdue -or $_.quantityRemaining -le 0 -or $_.status -in @('Completed','Cancelled') }).Count -eq 0) 'Report included a non-exception'
    $pagedReport = Request GET '/api/reports/order-exceptions?asOfDate=2026-11-02&pageSize=1'
    Assert ($pagedReport.Json.totals.orderCount -eq $report.Json.totals.orderCount) 'Report totals depend on page size'
    $sqlTotals = Sql "SELECT COUNT(*)||','||NVL(SUM(QuantityOrdered-QuantityCompleted),0) FROM ProductionOrders WHERE DueDate < DATE '2026-11-02' AND QuantityCompleted < QuantityOrdered AND Status NOT IN ('Completed','Cancelled');"
    Assert ($sqlTotals -eq "$($report.Json.totals.orderCount),$($report.Json.totals.quantityRemaining)") 'Report differs from independent database query'
    $noExceptions = Request GET '/api/reports/order-exceptions?asOfDate=1900-01-01'
    Assert ($noExceptions.Json.totals.orderCount -eq 0 -and $noExceptions.Json.totals.quantityRemaining -eq 0 -and $noExceptions.Json.items.Count -eq 0) 'Empty report failed'

    $body = @{ productId = $products.Json[0].productId; departmentId = $departments.Json[0].departmentId; quantityOrdered = 100; dueDate = '2026-11-15' }
    $created = Request POST '/api/orders' $body
    Assert ($created.Status -eq 201) 'Create failed'
    $createdId = [long]$created.Json.orderId
    Assert ($created.Headers.Location[0] -like "*/api/orders/$createdId") 'Create missing Location header'
    Assert ($created.Json.status -eq 'Planned' -and $created.Json.quantityCompleted -eq 0) 'Create defaults failed'
    $updated = Request PUT "/api/orders/$createdId" @{ quantityCompleted = 25; status = 'In Progress' }
    Assert ($updated.Status -eq 200 -and $updated.Json.quantityCompleted -eq 25 -and $updated.Json.status -eq 'In Progress') 'Update failed'
    $progressOnly = Request PUT "/api/orders/$createdId" @{ quantityCompleted = 50 }
    Assert ($progressOnly.Status -eq 200 -and $progressOnly.Json.status -eq 'In Progress') 'Progress update did not preserve status'
    Assert ((Sql "SELECT COUNT(*) FROM OrderStatusHistory WHERE OrderId=$createdId;") -eq '2') 'History trigger did not record only actual status changes'
    Assert ((Request PUT "/api/orders/$createdId" @{ quantityCompleted = 101 }).Status -eq 400) 'Excess completion accepted'
    Assert ((Request PUT "/api/orders/$createdId" @{ status = 'Completed' }).Status -eq 400) 'Incomplete completed order accepted'
    Assert ((Request GET "/api/orders/$createdId").Json.quantityCompleted -eq 50) 'Failed update changed saved order'
    Assert ((Request PUT "/api/orders/$createdId" @{}).Status -eq 400) 'Empty update accepted'
    Assert ((Request PUT "/api/orders/$createdId" @{ status = '' }).Status -eq 400) 'Empty status accepted'
    Assert ((Request PUT "/api/orders/$createdId" @{ quantityOrdered = 1 }).Status -eq 400) 'Unsupported field accepted'
    Assert ((Request PUT '/api/orders/999999999' @{ status = 'Released' }).Status -eq 404) 'Missing update should return 404'
    $completed = Request PUT "/api/orders/$createdId" @{ quantityCompleted = 100; status = 'Completed' }
    Assert ($completed.Status -eq 200 -and $completed.Json.quantityRemaining -eq 0) 'Completion failed'
    $body.productId = 999999999
    Assert ((Request POST '/api/orders' $body).Status -eq 400) 'Unknown product accepted'
    $body.productId = $products.Json[0].productId
    $body.quantityOrdered = 0
    Assert ((Request POST '/api/orders' $body).Status -eq 400) 'Zero quantity accepted'
    $body.quantityOrdered = 100
    $body.Remove('dueDate')
    Assert ((Request POST '/api/orders' $body).Status -eq 400) 'Missing due date accepted'
    Assert ((Request GET '/openapi/v1.json').Status -eq 200) 'OpenAPI failed'
    Write-Output "PASS: $checks integration checks."
}
finally {
    if ($null -ne $createdId) {
        $null = Sql "DELETE FROM OrderStatusHistory WHERE OrderId=$createdId;`nDELETE FROM ProductionOrders WHERE OrderId=$createdId;`nCOMMIT;"
        Write-Output "Removed test order $createdId and its history."
    }
}
