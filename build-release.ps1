[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root 'build'
$release = Join-Path $OutputRoot 'FocusQoS-2.2.0'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $framework)) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc = Join-Path $framework 'csc.exe'

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
if (Test-Path $release) { Remove-Item -LiteralPath $release -Recurse -Force }
New-Item -ItemType Directory -Path $build,$release | Out-Null

$app = Join-Path $build 'FocusQoS.exe'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:"$root\app\app.manifest" /out:"$app" `
    /reference:"$framework\System.dll" /reference:"$framework\System.Core.dll" /reference:"$framework\System.Drawing.dll" /reference:"$framework\System.Windows.Forms.dll" `
    "$root\app\Program.cs" "$root\app\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Uygulama derlenemedi.' }

$worker = Join-Path $build 'FocusQoSBenchmarkWorker.exe'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /out:"$worker" `
    /reference:"$framework\System.dll" /reference:"$framework\System.Core.dll" `
    "$root\worker\Program.cs" "$root\worker\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Benchmark işçisi derlenemedi.' }

$benchmark = Join-Path $build 'FocusQoSBenchmark.exe'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:"$root\app\app.manifest" /out:"$benchmark" `
    /reference:"$framework\System.dll" /reference:"$framework\System.Core.dll" /reference:"$framework\System.Drawing.dll" /reference:"$framework\System.Windows.Forms.dll" `
    "$root\benchmark\Program.cs" "$root\benchmark\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Benchmark uygulaması derlenemedi.' }

$setup = Join-Path $release 'FocusQoS-Setup-2.2.0.exe'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:"$root\installer\app.manifest" /out:"$setup" `
    /resource:"$app",OptimizerPayload /resource:"$benchmark",BenchmarkPayload /resource:"$worker",WorkerPayload `
    /reference:"$framework\System.dll" /reference:"$framework\System.Core.dll" /reference:"$framework\System.Drawing.dll" /reference:"$framework\System.Windows.Forms.dll" `
    "$root\installer\Setup.cs" "$root\installer\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Kurucu derlenemedi.' }

Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $release 'README.md')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination (Join-Path $release 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'RELEASE-NOTES.md') -Destination (Join-Path $release 'RELEASE-NOTES.md')
Copy-Item -LiteralPath (Join-Path $root 'SECURITY.md') -Destination (Join-Path $release 'SECURITY.md')

$hash = Get-FileHash -LiteralPath $setup -Algorithm SHA256
('{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $setup)) | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII

$zip = Join-Path $OutputRoot 'FocusQoS-2.2.0.zip'
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip -CompressionLevel Optimal

$sourceZip = Join-Path $OutputRoot 'FocusQoS-2.2.0-source.zip'
if (Test-Path $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
$sourceItems = @(
    (Join-Path $root 'app'), (Join-Path $root 'installer'), (Join-Path $root 'benchmark'), (Join-Path $root 'worker'),
    (Join-Path $root '.github'), (Join-Path $root 'tests'), (Join-Path $root 'README.md'),
    (Join-Path $root 'ARCHITECTURE.md'), (Join-Path $root 'SECURITY.md'), (Join-Path $root 'THREAT-MODEL.md'),
    (Join-Path $root 'CONTRIBUTING.md'), (Join-Path $root 'RELEASE-NOTES.md'), (Join-Path $root 'LICENSE.txt'),
    (Join-Path $root '.gitignore'), (Join-Path $root 'build-release.ps1')
)
Compress-Archive -Path $sourceItems -DestinationPath $sourceZip -CompressionLevel Optimal

Get-ChildItem -LiteralPath $release | Select-Object Name,Length
Get-Item -LiteralPath $zip,$sourceZip | Select-Object Name,Length
