#requires -Version 5.1
[CmdletBinding()]
param([switch]$RunTests, [switch]$Package, [switch]$SkipDesktopTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'artifacts'
$appOutput = Join-Path $build 'app'
$nativeOutput = Join-Path $build 'native'
$generated = Join-Path $build 'generated'
foreach ($directory in @($appOutput, $nativeOutput, $generated)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) {
    throw 'Install Visual Studio 2022 Build Tools with Desktop development with C++ and a Windows SDK.'
}
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'The Visual C++ x86/x64 build tools were not found.' }
$vcvars = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvars32.bat'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.8.' }

Write-Host 'Building the 32-bit RTSS client plugin...'
& (Join-Path $root 'scripts\Build-Native.cmd') $vcvars (Join-Path $root 'src\RTSSPlugin\DS4PCStats.cpp') $nativeOutput plugin
if ($LASTEXITCODE -ne 0) { throw 'The RTSS plugin build failed.' }
$plugin = Join-Path $nativeOutput 'DS4PCStats.dll'

# Each source build embeds its own plugin and verifies that exact payload at install time.
# Generate this file in artifacts so the tracked source remains unchanged.
$installer = [IO.File]::ReadAllText((Join-Path $root 'src\App\RtssPluginInstaller.cs'))
$pluginHash = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
$hashPattern = 'const string PayloadHash = "[A-F0-9]{64}";'
if ([regex]::Matches($installer, $hashPattern).Count -ne 1) { throw 'The plugin verification constant was not found.' }
$installer = [regex]::Replace($installer, $hashPattern, ('const string PayloadHash = "' + $pluginHash + '";'))
$generatedInstaller = Join-Path $generated 'RtssPluginInstaller.cs'
[IO.File]::WriteAllText($generatedInstaller, $installer)
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\App') -Filter '*.cs' -File |
    Where-Object Name -ne 'RtssPluginInstaller.cs' | Sort-Object Name | ForEach-Object FullName) + @($generatedInstaller)
$resources = @(
    ('/resource:' + (Join-Path $root 'assets\app.ico') + ',DS4StudioIcon'),
    ('/resource:' + (Join-Path $root 'assets\app-mark.png') + ',DS4StudioMark'),
    ('/resource:' + (Join-Path $root 'assets\controller.png') + ',DS4PopupControllerOutline'),
    ('/resource:' + $plugin + ',DS4PCStatsPlugin')
)
$app = Join-Path $appOutput 'DS4-Battery-Overlay.exe'
Write-Host 'Building DS4 Battery Overlay...'
& $compiler /nologo /target:winexe /platform:x64 /unsafe+ /optimize+ "/out:$app" "/win32icon:$root\assets\app.ico" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll @resources @sources
if ($LASTEXITCODE -ne 0) { throw 'The application build failed.' }

if ($RunTests) { & (Join-Path $root 'scripts\Test.ps1') -AppPath $app -VcVars $vcvars -SkipDesktopTests:$SkipDesktopTests }
if ($Package) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
    $release = Join-Path $root ('dist\DS4-Battery-Overlay-v' + $version + '-win-x64')
    $clientDirectory = Join-Path $release 'PCStats'
    New-Item -ItemType Directory -Path $clientDirectory -Force | Out-Null
    Copy-Item -LiteralPath $app -Destination $release -Force
    Copy-Item -LiteralPath $plugin -Destination $clientDirectory -Force
    Copy-Item -LiteralPath (Join-Path $root 'src\RTSSPlugin\DS4PCStats.cfg') -Destination $clientDirectory -Force
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE'),(Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $release -Force
    Copy-Item -LiteralPath (Join-Path $root 'docs\QUICKSTART.md') -Destination (Join-Path $release 'README.md') -Force
    $zip = $release + '.zip'
    Compress-Archive -LiteralPath (Join-Path $release 'DS4-Battery-Overlay.exe'),$clientDirectory,(Join-Path $release 'LICENSE'),(Join-Path $release 'THIRD-PARTY-NOTICES.md'),(Join-Path $release 'README.md') -DestinationPath $zip -Force
    $checksum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)
    [IO.File]::WriteAllText(($zip + '.sha256'), $checksum + "`n")
    Write-Host "Release package: $zip"
}
Write-Host "Application: $app"
