#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AppPath, [Parameter(Mandatory=$true)][string]$VcVars, [switch]$SkipDesktopTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $root 'artifacts\tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$checks = @('appearance','battery-scale','power','power-hold','events-export','startup','rtss','widget','studio-widget')
if ($SkipDesktopTests) {
    $checks = @($checks | Where-Object { $_ -notin @('rtss','widget','studio-widget') })
    Write-Host 'Skipping RTSS/widget desktop integration tests: these require a real interactive Windows desktop.'
}
foreach ($check in $checks) {
    $process = Start-Process -FilePath $AppPath -ArgumentList ('--' + $check + '-self-test') -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$check self-test failed with exit code $($process.ExitCode). See artifacts\app for its report." }
    Write-Host "PASS $check"
}
& (Join-Path $PSScriptRoot 'Build-Native.cmd') $VcVars (Join-Path $root 'tests\native\Tests.cpp') $testOutput tests
if ($LASTEXITCODE -ne 0) { throw 'Native tests could not be built.' }
& (Join-Path $testOutput 'Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'The native plugin tests failed.' }
