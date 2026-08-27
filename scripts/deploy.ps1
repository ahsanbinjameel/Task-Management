<#
.SYNOPSIS
    Builds a release of WorkflowApp and the idempotent SQL script that goes with it.

.DESCRIPTION
    This script deliberately stops short of touching the server. It produces the two artefacts a
    deployment needs — a publish folder and a reviewed-before-it-runs migration script — and leaves
    applying them to a human. Schema changes against a production database are the one step that
    should not be automated away behind a single command.

    See docs/03-RUNBOOK.md for what to do with the output.

.PARAMETER Output
    Where to write the publish folder. Defaults to ./publish.

.PARAMETER SkipTests
    Skip the test run. Use only when the suite has already passed on this exact commit.

.EXAMPLE
    ./scripts/deploy.ps1
    ./scripts/deploy.ps1 -Output C:\artifacts\workflowapp -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Output = "./publish",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($message) { Write-Host "`n=== $message ===" -ForegroundColor Cyan }

Step "Building the Angular client"
# The client compiles into src/WorkflowApp.Api/wwwroot, so it must run before dotnet publish
# collects static assets. A stale wwwroot is the classic way to ship yesterday's front end.
Push-Location (Join-Path $root "client")
try {
    if (-not (Test-Path "node_modules")) {
        Write-Host "Installing client dependencies..."
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed." }
    }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Angular build failed." }
}
finally { Pop-Location }

Step "Restoring and building"
dotnet build -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $SkipTests) {
    Step "Running tests"
    dotnet test -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Fix them before deploying." }
}

Step "Publishing to $Output"
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
dotnet publish src/WorkflowApp.Api -c Release --no-build -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# appsettings.Development.json carries a dev connection string and a dev signing key. It is never
# read in Production, but shipping it puts both on the server for no reason.
$devSettings = Join-Path $Output "appsettings.Development.json"
if (Test-Path $devSettings) {
    Remove-Item $devSettings -Force
    Write-Host "Removed appsettings.Development.json from the publish output."
}

Step "Generating the idempotent migration script"
$scriptPath = Join-Path $root "scripts/sql/release.sql"

$env:DOTNET_ROLL_FORWARD = "Major"
Push-Location (Join-Path $root "src/WorkflowApp.Api")
try {
    dotnet ef migrations script --idempotent `
        --project ../WorkflowApp.Infrastructure `
        --startup-project . `
        --output $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Could not generate the migration script. Is dotnet-ef installed? " +
              "dotnet tool install --global dotnet-ef --version 8.*"
    }
}
finally { Pop-Location }

Step "Done"
Write-Host "  Publish folder : $Output"
Write-Host "  SQL script     : $scriptPath"
Write-Host ""
Write-Host "Next, per docs/03-RUNBOOK.md section 3:" -ForegroundColor Yellow
Write-Host "  1. Review the SQL script, then apply it to the target database."
Write-Host "  2. Stop the site, copy the publish folder over it, start the site."
Write-Host "  3. Check /health and /health/ready."
Write-Host ""
Write-Host "Confirm these are set on the app pool before the first run:" -ForegroundColor Yellow
Write-Host "  ConnectionStrings__Default, Jwt__SigningKey, Workforce__TimeZoneId,"
Write-Host "  FileStorage__Root, Cors__Origins__0, ASPNETCORE_ENVIRONMENT=Production"
