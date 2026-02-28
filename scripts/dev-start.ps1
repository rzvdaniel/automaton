<#
.SYNOPSIS
    Start the Automaton worktree dev environment.

.DESCRIPTION
    1. Copies appsettings.Development.json from the main repo
    2. Starts Automaton.Studio.Server  (new terminal window)
    3. Starts Automaton.Runner         (new terminal window)
    4. Starts the Vue.js frontend      (new terminal window)

    AppData is NOT managed by this script. Copy app files manually into
    src\Automaton.Studio.Server\AppData as needed.

.PARAMETER MainRepo
    Path to the main Automaton repository.
    Auto-detected from git worktree list if not supplied.

.EXAMPLE
    .\scripts\dev-start.ps1
    .\scripts\dev-start.ps1 -MainRepo "C:\Work\automaton"
#>

param(
    [string]$MainRepo = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------- Resolve paths ---------------------------------------------------

$WorktreeRoot  = Split-Path $PSScriptRoot -Parent
$ServerProject = Join-Path $WorktreeRoot "src\Automaton.Studio.Server"
$RunnerProject = Join-Path $WorktreeRoot "src\Automaton.Runner"
$VueProject    = Join-Path $WorktreeRoot "src\automaton-studio-vue"

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

# ---------- 1. appsettings.Development.json ---------------------------------

Write-Step "Copying appsettings.Development.json from main repo..."

$AppSettingsSource = Join-Path $MainRepo "src\Automaton.Studio.Server\appsettings.Development.json"
$AppSettingsDest   = Join-Path $ServerProject "appsettings.Development.json"

if (-not (Test-Path $AppSettingsSource)) {
    Write-Warn "appsettings.Development.json not found in main repo."
    Write-Warn "Server will use defaults."
} else {
    Copy-Item $AppSettingsSource $AppSettingsDest -Force
    Write-Ok "Copied appsettings.Development.json"
}

# ---------- 2. Automaton.Studio.Server --------------------------------------

Write-Step "Starting Automaton.Studio.Server..."
Start-DevWindow "Automaton Server" $ServerProject "dotnet watch"
Write-Ok "Server window launched"

# Give the server time to start before the runner connects
Start-Sleep -Seconds 15

# ---------- 3. Automaton.Runner ---------------------------------------------

Write-Step "Starting Automaton.Runner..."
Start-DevWindow "Automaton Runner" $RunnerProject "dotnet watch"
Write-Ok "Runner window launched"

# ---------- 4. Vue.js frontend ----------------------------------------------

Write-Step "Starting Vue.js frontend..."
Start-DevWindow "Automaton Vue" $VueProject "npm run dev"
Write-Ok "Vue window launched"

# ---------- Summary ---------------------------------------------------------

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Automaton dev environment starting up    " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Server   : https://localhost:7091        " -ForegroundColor White
Write-Host "  Swagger  : https://localhost:7091/swagger" -ForegroundColor White
Write-Host "  Frontend : http://localhost:5173         " -ForegroundColor White
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""