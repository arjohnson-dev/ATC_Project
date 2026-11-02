$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $root 'Atc.Desktop/Atc.Desktop.vbproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
$apiUrl = if ($env:ATC_API_URL) { $env:ATC_API_URL.TrimEnd('/') } else { 'http://localhost:5080' }
$connected = $false
try {
    $null = Invoke-RestMethod "$apiUrl/api/departments" -TimeoutSec 3
    $connected = $true
} catch { }
if (-not $connected) {
    if ($apiUrl -ne 'http://localhost:5080') { throw "Start the configured API at $apiUrl before launching the tracker." }
    dotnet build (Join-Path $root 'Atc.Api/Atc.Api.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
    $oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $oldUrls = $env:ASPNETCORE_URLS
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = $apiUrl
        $apiProcess = Start-Process -FilePath (Join-Path $root 'Atc.Api/bin/Debug/net10.0/Atc.Api.exe') -WorkingDirectory (Join-Path $root 'Atc.Api') -WindowStyle Hidden -PassThru
    } finally {
        $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
        $env:ASPNETCORE_URLS = $oldUrls
    }
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($apiProcess.HasExited) { throw 'API exited. Check your Oracle connection and .NET user secrets.' }
        try {
            $null = Invoke-RestMethod "$apiUrl/api/departments" -TimeoutSec 1
            $connected = $true
            break
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $connected) { throw 'API did not become ready. Check Oracle and start the API manually to view its logs.' }
    Write-Output "Started local API (process $($apiProcess.Id)). It remains running when the desktop app closes."
}
Start-Process -FilePath (Join-Path $root 'Atc.Desktop/bin/Debug/net10.0-windows/Atc.Desktop.exe') -WorkingDirectory (Join-Path $root 'Atc.Desktop')
