#Requires -Version 5.1
<#
.SYNOPSIS
    Capture a clean screenshot of a single window (not the whole desktop).

.DESCRIPTION
    Finds a running process whose main window title matches -WindowTitle,
    brings it to the front, and saves a cropped PNG of just that window.
    Used to keep docs/screenshot.png up to date without a full-desktop grab
    that would leak whatever else is on screen.

    Run this while the app is open.

.EXAMPLE
    .\capture-screenshot.ps1

.EXAMPLE
    .\capture-screenshot.ps1 -WindowTitle "Miro" -Out ..\docs\screenshot.png

.EXAMPLE
    # See what window titles are available
    .\capture-screenshot.ps1 -List
#>
[CmdletBinding()]
param(
    [string] $WindowTitle = 'Miro',
    [string] $Out = (Join-Path $PSScriptRoot '..\docs\screenshot.png'),
    [int]    $DelaySeconds = 2,
    [switch] $List
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class WinCap
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
}
'@

function Get-CandidateWindows {
    Get-Process |
        Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle } |
        Select-Object ProcessName, MainWindowTitle, MainWindowHandle
}

if ($List) {
    Write-Host "Open windows with a title:`n" -ForegroundColor Cyan
    Get-CandidateWindows | Format-Table ProcessName, MainWindowTitle -AutoSize
    exit 0
}

$target = Get-CandidateWindows |
          Where-Object { $_.MainWindowTitle -like "*$WindowTitle*" } |
          Select-Object -First 1

if (-not $target) {
    Write-Host "No window title matched '*$WindowTitle*'." -ForegroundColor Red
    Write-Host "`nAvailable windows:" -ForegroundColor Yellow
    Get-CandidateWindows | Format-Table ProcessName, MainWindowTitle -AutoSize
    exit 1
}

Write-Host "[*] Target : $($target.ProcessName) - '$($target.MainWindowTitle)'" -ForegroundColor Cyan

# Bring it forward so nothing is drawn over it.
[void][WinCap]::ShowWindow($target.MainWindowHandle, 5)   # SW_SHOW
[void][WinCap]::SetForegroundWindow($target.MainWindowHandle)

if ($DelaySeconds -gt 0) {
    Write-Host "    waiting ${DelaySeconds}s for the window to settle..." -ForegroundColor DarkGray
    Start-Sleep -Seconds $DelaySeconds
}

$rect = New-Object WinCap+RECT
if (-not [WinCap]::GetWindowRect($target.MainWindowHandle, [ref]$rect)) {
    Write-Host "GetWindowRect failed." -ForegroundColor Red
    exit 1
}

$width  = $rect.Right  - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    Write-Host "Window has no area ($width x $height). Is it minimised?" -ForegroundColor Red
    exit 1
}

$outPath = [System.IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null

$bmp = New-Object System.Drawing.Bitmap $width, $height
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$gfx.Dispose()
$bmp.Dispose()

Write-Host "[+] Saved   : $outPath" -ForegroundColor Green
Write-Host "    Size    : ${width}x${height}" -ForegroundColor Gray
Write-Host "    Bytes   : $((Get-Item $outPath).Length)" -ForegroundColor Gray
