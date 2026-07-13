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

.EXAMPLE
    .\scripts\dev-start.ps1
    .\scripts\dev-start.ps1 -MainRepo "C:\Work\automaton"
#>

param(
    [string]$MainRepo = "",
    [switch]$AutoInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------- Resolve paths ---------------------------------------------------

$WorktreeRoot  = Split-Path $PSScriptRoot -Parent
$ServerProject = Join-Path $WorktreeRoot "src\Automaton.Api"
$VueProject    = Join-Path $WorktreeRoot "src\automaton-studio-vue"
$WebsiteProject = Join-Path $WorktreeRoot "src\automaton-vue"

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

Write-Step "Starting Automaton.Api..."
Start-DevWindow "Automaton Server" $ServerProject "dotnet watch"
Write-Ok "Server window launched"

# ---------- Node / Vite checks for frontend --------------------------------
$nodeOk = $null -ne (Get-Command node -ErrorAction SilentlyContinue)
$npmOk  = $null -ne (Get-Command npm -ErrorAction SilentlyContinue)
$studioViteBin = Join-Path $VueProject "node_modules\.bin\vite"
$websiteViteBin = Join-Path $WebsiteProject "node_modules\.bin\vite"
if (-not $nodeOk -or -not $npmOk) {
    Write-Warn "Node.js and/or npm not found. Vue frontends will not start."
    Write-Warn "Install Node.js (includes npm) from https://nodejs.org/"
} else {
    if (-not (Test-Path $studioViteBin)) {
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
    } else {
        Write-Ok "Studio frontend dependencies appear installed"
    }

    if (-not (Test-Path $websiteViteBin)) {
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
    } else {
        Write-Ok "Website frontend dependencies appear installed"
    }
}

# ---------- 3. Studio frontend ----------------------------------------------

Write-Step "Starting Automaton Studio frontend..."
Start-DevWindow "Automaton Studio" $VueProject "npm run dev"
Write-Ok "Studio window launched"

Write-Step "Starting front-facing Automaton website..."
Start-DevWindow "Automaton Website" $WebsiteProject "npm run dev"
Write-Ok "Website window launched"

# ---------- Summary ---------------------------------------------------------

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Automaton dev environment starting up    " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Server   : https://localhost:7091        " -ForegroundColor White
Write-Host "  Swagger  : https://localhost:7091/swagger" -ForegroundColor White
Write-Host "  Studio   : http://localhost:5173         " -ForegroundColor White
Write-Host "  Website  : http://localhost:5174         " -ForegroundColor White
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
