$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$credentialFile = Join-Path $projectRoot 'database/connection.local.txt'
$lines = Get-Content -LiteralPath $credentialFile
function Field([string]$Name) {
    $line = @($lines | Where-Object { $_.StartsWith("${Name}: ") })
    if ($line.Count -ne 1) { throw "Missing or duplicate credential field: $Name" }
    return $line[0].Substring($Name.Length + 2)
}
$dbHost = Field 'Host'
$dbPort = Field 'Port'
$dbService = Field 'Service name'
$dbUser = Field 'Username'
$dbPassword = Field 'Password'
# This local credential format was generated with alphanumeric/underscore values.
# Quote credentials so separators in future passwords remain within the value.
$quotedUser = '"' + $dbUser.Replace('"', '""') + '"'
$quotedPassword = '"' + $dbPassword.Replace('"', '""') + '"'
$connection = "User Id=$quotedUser;Password=$quotedPassword;Data Source=${dbHost}:${dbPort}/${dbService};"
@{ 'ConnectionStrings:Oracle' = $connection } | ConvertTo-Json -Compress |
    dotnet user-secrets set --project (Join-Path $projectRoot 'Atc.Api')
if ($LASTEXITCODE -ne 0) { throw 'Failed to configure .NET user secrets.' }
