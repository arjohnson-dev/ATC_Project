param([ValidateRange(1, 65535)][int]$Port = 5080)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'Atc.Api.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run this script from the published Backend directory.' }
# Run in Production explicitly, without relying on a developer launch profile.
& $exe --environment Production --urls "http://localhost:$Port"
if ($LASTEXITCODE -ne 0) { throw 'Backend exited with an error. Check configuration, Oracle, and whether the port is already in use.' }
