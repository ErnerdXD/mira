#Requires -Version 5.1
<#
.SYNOPSIS
    Build Miro.exe -- a small, double-clickable desktop app.

.DESCRIPTION
    Compiles app\Miro.cs into dist\Miro.exe using the C# compiler that is
    already part of Windows (.NET Framework's csc, reached through
    System.CodeDom). No Visual Studio, no Windows SDK, no NuGet, no internet.

    Output is a few tens of KB.

.PARAMETER Shortcut
    Also create Desktop and Start Menu shortcuts for Miro.exe.

.PARAMETER Run
    Launch Miro.exe after a successful build.

.EXAMPLE
    .\build-app.ps1 -Shortcut

.EXAMPLE
    .\build-app.ps1 -Run
#>
[CmdletBinding()]
param(
    [switch] $Shortcut,
    [switch] $Run
)

$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$src    = Join-Path $root 'app\Miro.cs'
$assemblyInfo = Join-Path $root 'app\AssemblyInfo.cs'
$ico    = Join-Path $root 'app\miro.ico'
$manifest = Join-Path $root 'app\Miro.manifest'
$outDir = Join-Path $root 'dist'
$exe    = Join-Path $outDir 'Miro.exe'

Write-Host "=== Miro build ===" -ForegroundColor Cyan
Write-Host "  source : $src"
Write-Host "  output : $exe"

if (-not (Test-Path $src)) { throw "Source not found: $src" }

# Regenerate the icon from source if the generator is available.
if (Test-Path (Join-Path $PSScriptRoot 'generate-assets.py')) {
    $py = (Get-Command python -ErrorAction SilentlyContinue).Source
    if ($py) {
        Write-Host "`n[*] Regenerating assets and icon..." -ForegroundColor DarkGray
        & $py (Join-Path $PSScriptRoot 'generate-assets.py') | Out-Null
    }
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# A running copy holds a lock on its own .exe. Warn, but keep going: the
# compile step below detects the lock (CS0016) and falls back to a side-by-side
# binary. Exiting here would skip that fallback entirely.
$running = Get-Process -Name Miro -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "`n[!] Miro is running (pid $($running.Id -join ', '))." -ForegroundColor Yellow
    Write-Host "    Its .exe is locked. Close it (tray icon -> Exit) to rebuild in place;" -ForegroundColor Yellow
    Write-Host "    otherwise a side-by-side Miro.new.exe will be produced." -ForegroundColor DarkGray
}

if (Test-Path $exe) {
    try {
        Remove-Item $exe -Force -ErrorAction Stop
    } catch {
        # Some environments (restricted shells, sandbox ACLs) grant Write but
        # deny Delete. The compiler can still overwrite the file in place, so
        # treat this as non-fatal and let the compile step decide.
        Write-Host "    note: old exe could not be deleted; overwriting in place" -ForegroundColor DarkGray
    }
}

# --------------------------------------------------------------------------- #
# Compile with the in-box C# compiler via CodeDom.
# --------------------------------------------------------------------------- #
function Invoke-Compile {
    param([Parameter(Mandatory)][string] $OutputPath)

    # Pin the IL to x64. AnyCPU would run as x86 on some hosts, and if this app
    # ever grows a native Miracast receiver that matters: the Miracast APIs
    # return MiracastNotSupported from an x86 process.
    $options = '/target:winexe /optimize+ /platform:x64 /nologo'
    if (Test-Path $ico)      { $options += " /win32icon:`"$ico`"" }
    if (Test-Path $manifest) { $options += " /win32manifest:`"$manifest`"" }

    $cp = New-Object System.CodeDom.Compiler.CompilerParameters
    $cp.GenerateExecutable      = $true
    $cp.GenerateInMemory        = $false
    $cp.OutputAssembly          = $OutputPath
    $cp.IncludeDebugInformation = $false
    $cp.TreatWarningsAsErrors   = $false
    $cp.CompilerOptions         = $options

    [void]$cp.ReferencedAssemblies.Add('System.dll')
    [void]$cp.ReferencedAssemblies.Add('System.Drawing.dll')
    [void]$cp.ReferencedAssemblies.Add('System.Windows.Forms.dll')
    # Used only for the parent-process diagnostic.
    [void]$cp.ReferencedAssemblies.Add('System.Management.dll')

    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider

    # AssemblyInfo supplies the exe's version resource: product name,
    # description, version. Without it Properties -> Details is blank.
    #
    # NOTE: the cast to [string[]] is required. PowerShell's @() builds an
    # Object[], which will not bind to the params string[] parameter and fails
    # with "The given path's format is not supported".
    [string[]]$sources = @($src)
    if (Test-Path $assemblyInfo) { $sources += $assemblyInfo }

    return $provider.CompileAssemblyFromFile($cp, $sources)
}

function Show-Errors($results) {
    foreach ($err in $results.Errors) {
        $colour = if ($err.IsWarning) { 'Yellow' } else { 'Red' }
        $kind   = if ($err.IsWarning) { 'warning' } else { 'error' }
        Write-Host ("  {0}: {1}" -f $kind, $err) -ForegroundColor $colour
    }
}

$results = Invoke-Compile -OutputPath $exe

# If the output file is locked by a running Miro, that surfaces as CS0016.
# Compile to a side-by-side name so the build still succeeds and the user can
# swap it in once they close the running copy.
if ($results.Errors.HasErrors) {
    $locked = $results.Errors | Where-Object {
        $_.ErrorNumber -eq 'CS0016' -or $_.ErrorText -match 'Could not write to output file'
    }

    if ($locked) {
        Write-Host "`n[!] $exe is locked by a running copy of Miro." -ForegroundColor Yellow
        Write-Host "    Close it (right-click the tray icon -> Exit), then rebuild." -ForegroundColor Yellow
        Write-Host "    Building a side-by-side copy for now..." -ForegroundColor DarkGray

        $fallback = Join-Path $outDir 'Miro.new.exe'
        $results  = Invoke-Compile -OutputPath $fallback
        if (-not $results.Errors.HasErrors) {
            Write-Host "`n[+] Built $fallback" -ForegroundColor Green
            Write-Host "    After closing the running Miro, replace it with:" -ForegroundColor Yellow
            Write-Host "      Move-Item '$fallback' '$exe' -Force" -ForegroundColor Gray
            exit 0
        }
    }
}

Show-Errors $results

if ($results.Errors.HasErrors) {
    Write-Host "`n[!] Build FAILED." -ForegroundColor Red
    exit 1
}

$size = (Get-Item $exe).Length
Write-Host ("`n[+] Built {0}  ({1:N1} KB)" -f $exe, ($size / 1KB)) -ForegroundColor Green

# --------------------------------------------------------------------------- #
# Shortcuts
# --------------------------------------------------------------------------- #
if ($Shortcut) {
    Write-Host "`n[*] Creating shortcuts..." -ForegroundColor Cyan

    # Writing to the Desktop or Start Menu can be denied (restricted shell,
    # sandbox, policy). That must not fail the build -- the exe is already good.
    try {
        $shell = New-Object -ComObject WScript.Shell

        $targets = @(
            (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Miro.lnk'),
            (Join-Path ([Environment]::GetFolderPath('Programs')) 'Miro.lnk')
        )

        foreach ($lnkPath in $targets) {
            try {
                $lnk = $shell.CreateShortcut($lnkPath)
                $lnk.TargetPath       = $exe
                $lnk.WorkingDirectory = $outDir
                $lnk.Description      = 'Miro - Miracast receiver for Windows'
                if (Test-Path $ico) { $lnk.IconLocation = $ico }
                $lnk.Save()
                Write-Host "    + $lnkPath" -ForegroundColor Green
            } catch {
                Write-Host "    ! could not write $lnkPath" -ForegroundColor Yellow
                Write-Host "      $($_.Exception.Message)" -ForegroundColor DarkGray
            }
        }
    } catch {
        Write-Host "    ! shortcut creation unavailable: $($_.Exception.Message)" -ForegroundColor Yellow
    } finally {
        if ($shell) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    }

    if (-not (Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Miro.lnk'))) {
        Write-Host "`n    To create a Desktop shortcut yourself, right-click dist\Miro.exe" -ForegroundColor DarkGray
        Write-Host "    and choose 'Send to > Desktop (create shortcut)'." -ForegroundColor DarkGray
    }
}

if ($Run) {
    Write-Host "`n[*] Launching..." -ForegroundColor Cyan
    Start-Process $exe
}

Write-Host @"

================================================================
 Miro.exe is ready.

   Double-click it, or find "Miro" in the Start menu if you
   built with -Shortcut.

   It launches Windows' Miracast receiver and walks you through
   connecting your phone.

================================================================
"@ -ForegroundColor White
