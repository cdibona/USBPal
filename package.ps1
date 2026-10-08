param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.1', [string]$Compiler)
$ErrorActionPreference = 'Stop'
if ((Get-Content "$PSScriptRoot/installer/USBPal.iss" -Raw) -notmatch "HasParameter\('/USBPALUPDATE'\)") { throw 'Installer must recognize the updater restart flag.' }
& "$PSScriptRoot/build.ps1" -Version $Version
if (-not $Compiler) { $Compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
if (-not (Test-Path -LiteralPath $Compiler)) { throw 'Install Inno Setup 6 or pass -Compiler with the path to ISCC.exe.' }
& $Compiler "/DAppVersion=$Version" "$PSScriptRoot/installer/USBPal.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$installer = Join-Path $PSScriptRoot "dist\USBPal-Setup-$Version-win-x64.exe"
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$installer.sha256" -Value "$hash  $([IO.Path]::GetFileName($installer))" -Encoding ascii
Write-Host "Installer: $installer"

# Stable asset name for GitHub's /releases/latest/download quickstart URL.
$latest = Join-Path $PSScriptRoot 'dist\USBPal-Setup-win-x64.exe'
Copy-Item -LiteralPath $installer -Destination $latest -Force
Set-Content -LiteralPath "$latest.sha256" -Value "$hash  $([IO.Path]::GetFileName($latest))" -Encoding ascii
