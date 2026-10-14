#Requires -Version 5.1
<#
.SYNOPSIS
    Install the toolchain needed to build and run Miro (a UWP app).

.DESCRIPTION
    Installs Visual Studio Community with the "Universal Windows Platform
    development" workload, which brings the Windows SDK and MSBuild with it.

    By default this opens the Visual Studio Installer GUI with the UWP workload
    already ticked, so you can see the size and confirm before anything is
    downloaded. Use -Quiet for an unattended install.

    The bootstrapper must already exist at:
      C:\Program Files (x86)\Microsoft Visual Studio\Installer\setup.exe
    It is usually present even when no VS product is installed.

    REQUIRES AN ELEVATED POWERSHELL. Expect roughly 8-15 GB of download.

.PARAMETER Quiet
    Run the install unattended (no GUI).

.PARAMETER Check
    Only report what is installed; change nothing.

.EXAMPLE
    .\install-toolchain.ps1 -Check

.EXAMPLE
    .\install-toolchain.ps1
#>
[CmdletBinding()]
param(
    [switch] $Quiet,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'

$InstallerRoot = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$Setup         = Join-Path $InstallerRoot 'setup.exe'
$VsWhere       = Join-Path $InstallerRoot 'vswhere.exe'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-VsState {
    $state = [ordered]@{
        Installer = Test-Path $Setup
        AnyVs     = $false
        Uwp       = $false
        Path      = $null
    }

    if (Test-Path $VsWhere) {
        $json = & $VsWhere -all -products * -format json 2>$null
        if ($json -and $json.Trim() -ne '[]') {
            $installs = $json | ConvertFrom-Json
            $state.AnyVs = $true
            $state.Path  = ($installs | Select-Object -First 1).installationPath

            $uwp = & $VsWhere -all -products * `
                     -requires Microsoft.VisualStudio.Workload.Universal `
                     -property installationPath 2>$null
            $state.Uwp = [bool]$uwp
        }
    }
    return $state
}

function Show-State($s) {
    Write-Host "`n=== Toolchain state ===" -ForegroundColor Cyan
    Write-Host ("  VS Installer bootstrapper : {0}" -f $(if ($s.Installer) { 'present' } else { 'MISSING' }))
    Write-Host ("  Any Visual Studio install : {0}" -f $(if ($s.AnyVs)   { $s.Path } else { 'none' }))
    Write-Host ("  UWP development workload  : {0}" -f $(if ($s.Uwp)     { 'installed' } else { 'NOT installed' }))

    $winsdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue
    Write-Host ("  Windows SDK versions      : {0}" -f $(if ($winsdk) { ($winsdk.Name -join ', ') } else { 'none' }))

    try {
        $c = Get-PSDrive C
        Write-Host ("  Free space on C:          : {0} GB" -f [math]::Round($c.Free / 1GB, 1))
    } catch { }

    $devMode = $null
    try {
        $devMode = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
                     -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
    } catch { }
    Write-Host ("  Developer Mode            : {0}" -f $(if ($devMode -eq 1) { 'enabled' } else { 'DISABLED (required to deploy)' }))
}

$state = Get-VsState
Show-State $state

if ($Check) { exit 0 }

if (-not $state.Installer) {
    Write-Host "`nThe Visual Studio Installer bootstrapper is missing." -ForegroundColor Red
    Write-Host "Download it from https://visualstudio.microsoft.com/downloads/ and re-run." -ForegroundColor Yellow
    exit 1
}

if ($state.Uwp) {
    Write-Host "`n[=] The UWP workload is already installed." -ForegroundColor Green
    Write-Host "    Next: open MirrorReceiver.sln and press F5." -ForegroundColor Green
    exit 0
}

if (-not (Test-Admin)) {
    Write-Host "`nThis needs Administrator to install Visual Studio." -ForegroundColor Red
    Write-Host "Right-click PowerShell -> 'Run as Administrator', then re-run." -ForegroundColor Yellow
    exit 1
}

# --------------------------------------------------------------------------- #
# Enable Developer Mode -- required before Visual Studio will deploy a UWP app.
# --------------------------------------------------------------------------- #
if ($devMode -ne 1) {
    Write-Host "`n[*] Enabling Developer Mode..." -ForegroundColor Cyan
    try {
        $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
        if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
        Set-ItemProperty -Path $key -Name AllowDevelopmentWithoutDevLicense -Value 1 -Type DWord
        Write-Host "    enabled" -ForegroundColor Green
    } catch {
        Write-Host "    could not enable automatically: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "    Do it manually: Settings > System > For developers > Developer Mode = On" -ForegroundColor Yellow
    }
}

# --------------------------------------------------------------------------- #
# Install Visual Studio + the UWP workload.
# --------------------------------------------------------------------------- #
Write-Host "`n[*] Launching the Visual Studio Installer with the UWP workload selected" -ForegroundColor Cyan
Write-Host "    Product : Visual Studio Community" -ForegroundColor DarkGray
Write-Host "    Workload: Universal Windows Platform development" -ForegroundColor DarkGray
Write-Host "    Size    : roughly 8-15 GB" -ForegroundColor DarkGray

$args = @(
    'install'
    '--productId', 'Microsoft.VisualStudio.Product.Community'
    '--add',       'Microsoft.VisualStudio.Workload.Universal'
    '--includeRecommended'
    '--norestart'
)

if ($Quiet) {
    $args += @('--quiet', '--wait')
    Write-Host "`n    running unattended; this can take 20-40 minutes..." -ForegroundColor Yellow
} else {
    Write-Host "`n    The installer window will open. Review it and click Install." -ForegroundColor Yellow
}

$proc = Start-Process -FilePath $Setup -ArgumentList $args -PassThru -Wait
Write-Host "`n    installer exited with code $($proc.ExitCode)" -ForegroundColor DarkGray

# --------------------------------------------------------------------------- #
Show-State (Get-VsState)

Write-Host @"

================================================================
 NEXT
================================================================
 1. Open the solution:
        .\MirrorReceiver.sln

 2. Set the platform dropdown to x64 (top toolbar).

 3. Press F5.
    Visual Studio builds, deploys and launches Miro automatically.

 4. Miro now appears in the Start menu. Search "Miro".

 5. While it is running, capture a screenshot for the README:
        .\scripts\capture-screenshot.ps1 -WindowTitle "Miro"

================================================================
"@ -ForegroundColor White
