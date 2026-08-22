<#
.SYNOPSIS
    Installs a staged publish folder into an IIS site. Must be run elevated.

.DESCRIPTION
    The companion to deploy.ps1, which produces the artefacts but touches nothing. This one does the
    parts that need administrator rights: stopping the app pool so the DLLs are not locked, copying
    the build in, creating the attachment store, granting the app pool identity the access it needs,
    and starting the pool again.

    It deliberately does NOT apply migrations. Schema changes go in separately and first, so a bad
    one is caught before the binaries move. See docs/03-RUNBOOK.md section 3.

    web.config is copied from the staged folder, so whatever environment variables were configured
    there survive the deployment. That is the one file worth checking before you run this.

.EXAMPLE
    # From an elevated PowerShell, in the repo root:
    ./scripts/install-iis.ps1 -Source .\publish -Target C:\inetpub\wwwroot\publish
#>
[CmdletBinding()]
param(
    [string]$Source = ".\publish",
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$AppPool = "DefaultAppPool",
    [string]$StorageRoot = "C:\WorkflowApp\storage"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Run this from an elevated PowerShell (Run as administrator)." }

if (-not (Test-Path $Source)) { throw "Staged publish folder not found: $Source. Run scripts/deploy.ps1 first." }
if (-not (Test-Path (Join-Path $Source "web.config"))) { throw "No web.config in $Source." }

Import-Module WebAdministration

function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

$identity = "IIS APPPOOL\$AppPool"

Step "Stopping app pool '$AppPool'"
if ((Get-WebAppPoolState -Name $AppPool).Value -ne "Stopped") {
    Stop-WebAppPool -Name $AppPool
    # The worker process does not exit instantly, and a copy over a locked DLL fails.
    $waited = 0
    while ((Get-WebAppPoolState -Name $AppPool).Value -ne "Stopped" -and $waited -lt 30) {
        Start-Sleep -Seconds 1; $waited++
    }
    Write-Host "Stopped after ${waited}s."
}

Step "Copying $Source -> $Target"
if (-not (Test-Path $Target)) { New-Item -ItemType Directory -Path $Target -Force | Out-Null }
Copy-Item -Path (Join-Path $Source "*") -Destination $Target -Recurse -Force

# stdoutLogEnabled writes here. Create it now rather than discovering the permission problem
# during the first failed startup, when the log is the only thing that would have told you why.
$logs = Join-Path $Target "logs"
if (-not (Test-Path $logs)) { New-Item -ItemType Directory -Path $logs -Force | Out-Null }

Step "Creating the attachment store at $StorageRoot"
if (-not (Test-Path $StorageRoot)) { New-Item -ItemType Directory -Path $StorageRoot -Force | Out-Null }

Step "Granting '$identity' access"
# Read+execute over the application, modify over the two directories it writes to.
icacls $Target /grant "${identity}:(OI)(CI)(RX)" /T /C /Q | Out-Null
icacls $logs /grant "${identity}:(OI)(CI)(M)" /T /C /Q | Out-Null
icacls $StorageRoot /grant "${identity}:(OI)(CI)(M)" /T /C /Q | Out-Null

Step "Starting app pool"
Start-WebAppPool -Name $AppPool
Start-Sleep -Seconds 3
Write-Host "App pool state: $((Get-WebAppPoolState -Name $AppPool).Value)"

Step "Done"
Write-Host "Check the site, then:"
Write-Host "  /health        - liveness"
Write-Host "  /health/ready  - proves the database is reachable"
Write-Host ""
Write-Host "If it still 500.30s, the reason is now in $logs\stdout_*.log" -ForegroundColor Yellow
