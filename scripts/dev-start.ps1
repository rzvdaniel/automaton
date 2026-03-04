<#
.SYNOPSIS
    Start the Automaton worktree dev environment.

.DESCRIPTION
    1. Copies appsettings.Development.json from the main repo
    2. Starts Automaton.Studio.Server  (new terminal window)
    3. Starts FastAPI backend          (new terminal window, port 8001)
    4. Starts Automaton.Runner         (new terminal window)
    5. Starts the Vue.js frontend      (new terminal window)

    Requires Python with fastapi and uvicorn installed:
      pip install fastapi "uvicorn[standard]"

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

# ---------- 3. FastAPI backend (required for tunnel execution) ---------------

# Check Python and required packages are installed
Write-Step "Checking FastAPI prerequisites..."
$pythonOk  = $null -ne (Get-Command python -ErrorAction SilentlyContinue)
$fastapiOk = $false
$uvicornOk = $false
if (-not $pythonOk) {
    Write-Warn "Python not found. FastAPI backend will not start."
    Write-Warn "Install Python from https://python.org, then run:"
    Write-Warn "  pip install fastapi `"uvicorn[standard]`""
} else {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    python -c "import fastapi" 2>&1 | Out-String | Out-Null; $fastapiOk = ($LASTEXITCODE -eq 0)
    python -c "import uvicorn" 2>&1 | Out-String | Out-Null; $uvicornOk = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prev
    if (-not $fastapiOk -or -not $uvicornOk) {
        Write-Warn "FastAPI or uvicorn not installed. App controller execution will not work."
        Write-Warn "Run once to fix:  pip install fastapi `"uvicorn[standard]`""
    } else {
        Write-Ok "FastAPI prerequisites OK"
    }
}

Write-Step "Starting FastAPI backend..."
$FastApiDir     = Join-Path $RunnerProject "fastapi_server"
# Stable path at the worktree root - shared by FastAPI and the C# runner via RUNNER_SCRIPTS_DIR
$RunnerDataDir  = Join-Path $WorktreeRoot "runner-data\apps"
New-Item -ItemType Directory -Force -Path $RunnerDataDir | Out-Null
# Set once here - all child windows inherit these automatically
$env:RUNNER_SCRIPTS_DIR    = $RunnerDataDir
# Allows the runner to connect to localhost using the ASP.NET self-signed dev certificate
$env:DOTNET_ENVIRONMENT    = "Development"
if ($pythonOk -and $fastapiOk -and $uvicornOk) {
    Start-DevWindow "Automaton FastAPI" $FastApiDir "python main.py"
    Write-Ok "FastAPI window launched (port 8001)"
    # Give FastAPI a moment to start before the runner connects
    Start-Sleep -Seconds 3
} else {
    Write-Warn "Skipping FastAPI window - prerequisites not met."
}

# ---------- 4. Automaton.Runner ---------------------------------------------

Write-Step "Starting Automaton.Runner..."
Start-DevWindow "Automaton Runner" $RunnerProject "dotnet watch"
Write-Ok "Runner window launched"

# ---------- 5. Vue.js frontend ----------------------------------------------

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