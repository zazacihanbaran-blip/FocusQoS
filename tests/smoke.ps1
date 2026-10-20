[CmdletBinding()]
param([string]$DistRoot = (Join-Path $PSScriptRoot '..\dist'))

$ErrorActionPreference = 'Stop'
$release = Join-Path $DistRoot 'FocusQoS-2.2.0'
$setup = Join-Path $release 'FocusQoS-Setup-2.2.0.exe'
$hashFile = Join-Path $release 'SHA256SUMS.txt'

if (-not (Test-Path $setup)) { throw "Setup missing: $setup" }
if (-not (Test-Path $hashFile)) { throw "Hash file missing: $hashFile" }

$expected = (Get-Content -LiteralPath $hashFile).Split(' ')[0]
$actual = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw 'SHA-256 mismatch.' }

$bytes = [IO.File]::ReadAllBytes($setup)
$peOffset = [BitConverter]::ToInt32($bytes, 60)
$optionalHeader = $peOffset + 24
$subsystem = [BitConverter]::ToUInt16($bytes, $optionalHeader + 68)
if ($subsystem -ne 2) { throw "Expected Windows GUI subsystem (2), got $subsystem" }

$text = [Text.Encoding]::UTF8.GetString($bytes)
foreach ($resource in @('OptimizerPayload','BenchmarkPayload','WorkerPayload')) {
    if (-not $text.Contains($resource)) { throw "Embedded resource missing: $resource" }
}

Write-Host "Smoke tests passed. SHA256=$actual"

