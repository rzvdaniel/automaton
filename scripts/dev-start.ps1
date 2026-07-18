<#
.SYNOPSIS
    Start the Automaton worktree dev environment.

.DESCRIPTION
    1. Copies appsettings.Development.json from the main repo
    2. Starts Automaton.Api  (new terminal window)
    3. Starts the Studio Vue frontend (new terminal window)
    4. Starts the public website app  (new terminal window)

    Documentation servers are not started by default.
    Start them manually only when working on docs:
      public docs:  cd src\automaton-vue && npm run docs:dev

    If you pass `-AutoInstall` the script will attempt to run `npm install` in
    the Studio and Website projects when frontend dependencies are missing.

    By default the API, Studio, and Website are started. If you pass one or
    more selective switches, only the selected services are started. The
    switches can be combined, for example:
      .\scripts\dev-start.ps1 -Api -Studio

        Note: The script will NOT install Node.js itself; install Node from
        https://nodejs.org if not present.

    AppData is NOT managed by this script. Copy app files manually into
    src\Automaton.Api\AppData as needed.

.PARAMETER MainRepo
    Path to the main Automaton repository.
    Auto-detected from git worktree list if not supplied.

.PARAMETER AutoInstall
    When supplied the script will attempt to auto-install missing frontend
    dependencies (`npm install`) if npm is available.
    Use this for convenience on developer machines. Omit to require manual
    installation in controlled environments.

.PARAMETER Api
    Include only Automaton.Api in a selective startup.

.PARAMETER Studio
    Include only the Studio frontend in a selective startup.

.PARAMETER Website
    Include only the public Website frontend in a selective startup.

.EXAMPLE
    .\scripts\dev-start.ps1
    .\scripts\dev-start.ps1 -MainRepo "C:\Work\automaton"
#>

param(
    [string]$MainRepo = "",
    [switch]$AutoInstall,
    [switch]$Api,
    [switch]$Studio,
    [switch]$Website
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------- Resolve paths ---------------------------------------------------

$WorktreeRoot  = Split-Path $PSScriptRoot -Parent
$ServerProject = Join-Path $WorktreeRoot "src\Automaton.Api"
$VueProject    = Join-Path $WorktreeRoot "src\automaton-studio-vue"
$WebsiteProject = Join-Path $WorktreeRoot "src\automaton-vue"

$selectiveStartup = $Api -or $Studio -or $Website
$startApi = -not $selectiveStartup -or $Api
$startStudio = -not $selectiveStartup -or $Studio
$startWebsite = -not $selectiveStartup -or $Website
$startedServices = [System.Collections.Generic.List[string]]::new()

# Auto-detect main repo from git worktree list if not supplied
if (-not $MainRepo) {
    $worktrees = git -C $WorktreeRoot worktree list 2>$null
    $mainLine  = ($worktrees | Select-Object -First 1) -replace '\s+\w+\s+\[.*\]', ''
    $MainRepo  = $mainLine.Trim()
}

# ---------- Helpers ---------------------------------------------------------

function Write-Step([string]$msg) { Write-Host "[>] $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "[!] $msg" -ForegroundColor Yellow }

function Start-DevWindow([string]$title, [string]$workDir, [string]$cmd) {
    $escaped = $cmd -replace "'", "''"
    Start-Process powershell -ArgumentList "-NoExit", "-Command", `
        "`$host.UI.RawUI.WindowTitle = '$title'; Set-Location '$workDir'; $escaped"
}

# ---------- 2. Automaton.Api --------------------------------------

if ($startApi) {
    Write-Step "Starting Automaton.Api..."
    Start-DevWindow "Automaton Server" $ServerProject "dotnet watch"
    $startedServices.Add("API")
    Write-Ok "Server window launched"
}

# ---------- Node / Vite checks for frontend --------------------------------
$startFrontend = $startStudio -or $startWebsite
if ($startFrontend) {
    $nodeOk = $null -ne (Get-Command node -ErrorAction SilentlyContinue)
    $npmOk  = $null -ne (Get-Command npm -ErrorAction SilentlyContinue)
    $studioViteBin = Join-Path $VueProject "node_modules\.bin\vite"
    $websiteViteBin = Join-Path $WebsiteProject "node_modules\.bin\vite"
    if (-not $nodeOk -or -not $npmOk) {
    Write-Warn "Node.js and/or npm not found. Vue frontends will not start."
    Write-Warn "Install Node.js (includes npm) from https://nodejs.org/"
    } else {
    if ($startStudio -and -not (Test-Path $studioViteBin)) {
        if ($AutoInstall) {
            Write-Step "Installing studio frontend dependencies (npm install)..."
            $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            try {
                Push-Location $VueProject
                npm install
                Pop-Location
                Write-Ok "Studio frontend npm install completed"
            } catch {
                Write-Warn "Studio frontend npm install failed: $_"
                Pop-Location -ErrorAction SilentlyContinue
            } finally { $ErrorActionPreference = $prev }
        } else {
            Write-Warn "Frontend dependencies missing. Run 'npm install' in $VueProject or re-run with -AutoInstall"
        }
    } elseif ($startStudio) {
        Write-Ok "Studio frontend dependencies appear installed"
    }

    if ($startWebsite -and -not (Test-Path $websiteViteBin)) {
        if ($AutoInstall) {
            Write-Step "Installing website frontend dependencies (npm install)..."
            $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            try {
                Push-Location $WebsiteProject
                npm install
                Pop-Location
                Write-Ok "Website frontend npm install completed"
            } catch {
                Write-Warn "Website frontend npm install failed: $_"
                Pop-Location -ErrorAction SilentlyContinue
            } finally { $ErrorActionPreference = $prev }
        } else {
            Write-Warn "Website frontend dependencies missing. Run 'npm install' in $WebsiteProject or re-run with -AutoInstall"
        }
    } elseif ($startWebsite) {
        Write-Ok "Website frontend dependencies appear installed"
    }
    }
}

# ---------- 3. Studio frontend ----------------------------------------------

if ($startStudio) {
    Write-Step "Starting Automaton Studio frontend..."
    Start-DevWindow "Automaton Studio" $VueProject "npm run dev"
    $startedServices.Add("Studio")
    Write-Ok "Studio window launched"
}

if ($startWebsite) {
    Write-Step "Starting front-facing Automaton website..."
    Start-DevWindow "Automaton Website" $WebsiteProject "npm run dev"
    $startedServices.Add("Website")
    Write-Ok "Website window launched"
}

# ---------- Summary ---------------------------------------------------------

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Automaton dev environment starting up    " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Started  : $($startedServices -join ', ')" -ForegroundColor White
if ($startApi) {
    Write-Host "  Server   : https://localhost:7091        " -ForegroundColor White
    Write-Host "  Swagger  : https://localhost:7091/swagger" -ForegroundColor White
}
if ($startStudio) {
    Write-Host "  Studio   : http://localhost:5173         " -ForegroundColor White
}
if ($startWebsite) {
    Write-Host "  Website  : http://localhost:5174         " -ForegroundColor White
}
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
