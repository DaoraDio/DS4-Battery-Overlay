#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'artifacts\app\DS4-Battery-Overlay.exe'
if (!(Test-Path -LiteralPath $app)) { throw 'Run .\Build.ps1 first.' }
$output = Join-Path $root 'artifacts\docs'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$renderer = Join-Path $output 'RenderDocs.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe "/out:$renderer" /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'RenderDocs.cs')
if ($LASTEXITCODE -ne 0) { throw 'The documentation renderer could not be built.' }
& $renderer $app $root
if ($LASTEXITCODE -ne 0) { throw 'The documentation images could not be rendered.' }
Write-Host 'Documentation images saved to docs\images.'
