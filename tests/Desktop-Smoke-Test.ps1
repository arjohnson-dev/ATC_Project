$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runDirectory = Join-Path $root ('test-results/desktop-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$env:ATC_TEST_OUTPUT = $runDirectory
$createdIdFile = Join-Path $runDirectory 'created-order-id.txt'
try {
    dotnet run --project (Join-Path $root 'Atc.Desktop') --no-build -- --smoke-test
    $testExitCode = $LASTEXITCODE
    $result = Join-Path $runDirectory 'result.txt'
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
    else { throw 'Desktop test exited without a result file.' }
    if ($testExitCode -ne 0) { throw 'Desktop integration failed; see result.txt.' }
    Write-Output "Screenshots and CSV: $runDirectory"
}
finally {
    if (Test-Path -LiteralPath $createdIdFile) {
        $orderId = [long](Get-Content -LiteralPath $createdIdFile -Raw)
        if ($orderId -le 0) { throw 'Invalid test order ID; refusing cleanup.' }
        $credentialPath = Join-Path $root 'database/connection.local.txt'
        $password = ((Get-Content -LiteralPath $credentialPath) | Where-Object { $_ -like 'Password: *' }).Substring(10)
        @"
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
CONNECT ATC_APP/"$password"@localhost:1521/FREEPDB1
SET FEEDBACK OFF
DELETE FROM OrderStatusHistory WHERE OrderId=$orderId;
DELETE FROM ProductionOrders WHERE OrderId=$orderId;
COMMIT;
EXIT
"@ | sqlplus -s /nolog
        if ($LASTEXITCODE -ne 0) { throw 'Test order cleanup failed.' }
        Write-Output "Removed desktop test order $orderId and its history."
    }
    Remove-Item Env:ATC_TEST_OUTPUT -ErrorAction SilentlyContinue
}
