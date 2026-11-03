param([switch]$ConfigureLocalDatabase)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/win-x64'
foreach ($project in @('Atc.Api/Atc.Api.csproj', 'Atc.Desktop/Atc.Desktop.vbproj')) {
    dotnet publish (Join-Path $root $project) -p:PublishProfile=WindowsX64
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
# Keep a working local configuration across repeat publishes. Never embed a
# password in an EXE, sample config, or shareable archive.
$backend = Join-Path $output 'Backend'
$sample = @{
    Urls = 'http://localhost:5080'
    ConnectionStrings = @{
        Oracle = 'User Id=ATC_APP;Password=REPLACE_WITH_DATABASE_PASSWORD;Data Source=localhost:1521/FREEPDB1;'
    }
}
$sample | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backend 'appsettings.Production.example.json') -Encoding utf8
if ($ConfigureLocalDatabase) {
    $lines = Get-Content -LiteralPath (Join-Path $root 'database/connection.local.txt')
    function Field([string]$Name) {
        $matches = @($lines | Where-Object { $_.StartsWith("${Name}: ") })
        if ($matches.Count -ne 1) { throw "Missing or duplicate credential field: $Name" }
        return $matches[0].Substring($Name.Length + 2)
    }
    $dbUser = '"' + (Field 'Username').Replace('"', '""') + '"'
    $dbPassword = '"' + (Field 'Password').Replace('"', '""') + '"'
    $dbHost = Field 'Host'
    $dbPort = Field 'Port'
    $dbService = Field 'Service name'
    $sample.ConnectionStrings.Oracle = "User Id=$dbUser;Password=$dbPassword;Data Source=${dbHost}:${dbPort}/${dbService};"
    $sample | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backend 'appsettings.Production.json') -Encoding utf8
    Write-Output 'Configured the published backend for your local database. appsettings.Production.json contains local credentials; do not share it.'
}
Copy-Item -LiteralPath (Join-Path $root 'scripts/Run-PublishedBackend.ps1') -Destination (Join-Path $backend 'Start-Backend.ps1') -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts/PUBLISHED-README.md') -Destination (Join-Path $output 'README.md') -Force
Get-Item (Join-Path $output 'Frontend/Atc.Desktop.exe'), (Join-Path $output 'Backend/Atc.Api.exe') |
    Select-Object FullName, @{Name='SizeMB'; Expression={ [math]::Round($_.Length / 1MB, 1) }}
