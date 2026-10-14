#Requires -Version 5.1
<#
.SYNOPSIS
    Enable Windows' built-in Miracast receiver ("Wireless Display" / Connect app).

.DESCRIPTION
    Windows 10/11 can already act as a Miracast sink through an optional feature.
    Turning it on is the fastest way to confirm that your PC's Wi-Fi adapter and
    your phone both support Miracast -- before you spend time building anything.

    If this works, you have a working receiver in about five minutes.
    If it does not, no amount of application code will fix it: the limitation is
    in your Wi-Fi driver or your phone.

    REQUIRES AN ELEVATED (Administrator) POWERSHELL.

.PARAMETER Uninstall
    Remove the Wireless Display capability.

.PARAMETER StatusOnly
    Report state without changing anything.

.EXAMPLE
    .\Enable-BuiltInReceiver.ps1

.EXAMPLE
    .\Enable-BuiltInReceiver.ps1 -StatusOnly
#>
[CmdletBinding()]
param(
    [switch] $Uninstall,
    [switch] $StatusOnly
)

$ErrorActionPreference = 'Stop'
$Capability = 'App.WirelessDisplay.Connect~~~~0.0.1.0'

function Assert-Admin {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "This script needs Administrator." -ForegroundColor Red
        Write-Host "Right-click PowerShell -> 'Run as Administrator', then re-run." -ForegroundColor Yellow
        exit 1
    }
}

function Show-Status {
    Write-Host "`n=== Wireless Display capability ===" -ForegroundColor Cyan
    $cap = Get-WindowsCapability -Online -Name $Capability -ErrorAction SilentlyContinue
    if ($cap) {
        $colour = if ($cap.State -eq 'Installed') { 'Green' } else { 'Yellow' }
        Write-Host ("  {0}  ->  {1}" -f $cap.Name, $cap.State) -ForegroundColor $colour
        if ($cap.State -eq 'InstallPending') {
            Write-Host "  A REBOOT is required to finish installing." -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Capability not found on this build." -ForegroundColor Yellow
        Write-Host "  Install 'Wireless Display' via Settings > System > Optional features." -ForegroundColor DarkGray
    }

    Write-Host "`n=== Wi-Fi adapter (Miracast needs Wi-Fi Direct) ===" -ForegroundColor Cyan
    try {
        $wifi = Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
                Where-Object { $_.InterfaceDescription -match 'Wi-?Fi|Wireless|802\.11' }
        if ($wifi) {
            $wifi | Select-Object Name, InterfaceDescription, Status, LinkSpeed |
                Format-Table -AutoSize | Out-String | Write-Host
        } else {
            Write-Host "  No wireless adapter found. Miracast requires Wi-Fi." -ForegroundColor Red
        }
    } catch {
        Write-Host "  Could not enumerate adapters: $($_.Exception.Message)" -ForegroundColor DarkGray
    }

    Write-Host "`n=== Receiver process ===" -ForegroundColor Cyan
    $proc = Get-Process -Name 'WUDFHost','Microsoft.PPIProjection' -ErrorAction SilentlyContinue
    if ($proc) { $proc | Select-Object Name, Id | Format-Table -AutoSize | Out-String | Write-Host }
    else { Write-Host "  Connect app not currently running." -ForegroundColor DarkGray }
}

Assert-Admin

if ($StatusOnly) { Show-Status; exit 0 }

if ($Uninstall) {
    Write-Host "[*] Removing Wireless Display capability..." -ForegroundColor Yellow
    Remove-WindowsCapability -Online -Name $Capability | Out-Null
    Write-Host "[+] Removed." -ForegroundColor Green
    Show-Status
    exit 0
}

Write-Host "`n[*] Installing Windows' built-in Miracast receiver" -ForegroundColor Cyan
$before = Get-WindowsCapability -Online -Name $Capability -ErrorAction SilentlyContinue

if ($before -and $before.State -eq 'Installed') {
    Write-Host "[=] Already installed." -ForegroundColor Green
} else {
    Write-Host "    downloading from Windows Update (needs internet, can take several minutes)..." -ForegroundColor DarkGray
    try {
        $result = Add-WindowsCapability -Online -Name $Capability
        Write-Host "[+] RestartNeeded = $($result.RestartNeeded)" -ForegroundColor Green
    } catch {
        Write-Host "[!] Install failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "    Try the GUI: Settings > System > Optional features > Add a feature" -ForegroundColor Yellow
        Write-Host "    Search for 'Wireless Display' and install from there." -ForegroundColor Yellow
        exit 1
    }
}

Show-Status

Write-Host @"

================================================================
 NEXT
================================================================
 1. If a reboot is pending, reboot first.

 2. Set the receiving mode (one time):
        Settings > System > Projecting to this PC
    Set both dropdowns to "Available everywhere on secure networks".

 3. On this PC open the Connect app:
        Start menu -> type "Connect"

 4. On your Android phone:
        Settings > Connections > Cast / Smart View /
        Wireless display / Screen mirroring
    Pick this PC from the list.

 If the PC never appears on the phone:
   - both devices must be on the same Wi-Fi network (or a hotspot)
   - check the adapter output above for Wi-Fi Direct support
   - a phone that dropped Miracast (e.g. Google Pixel) will not work

================================================================
"@ -ForegroundColor White
