<#
.SYNOPSIS
    Start the Automaton worktree dev environment.

.DESCRIPTION
    1. Copies appsettings.Development.json from the main repo
    2. Starts Automaton.Api  (new terminal window)
    3. Starts FastAPI backend          (new terminal window, port 8001)
    4. Starts Automaton.Runner         (new terminal window)
    5. Starts the Vue.js frontend      (new terminal window)

    Requires Python with fastapi and uvicorn installed:
      pip install fastapi "uvicorn[standard]"

        New: If you pass `-AutoInstall` the script will attempt to install missing
        Python and frontend packages when possible:
            - Uses `python -m pip install fastapi "uvicorn[standard]"` when Python
                is present but FastAPI/uvicorn are missing.
            - Runs `npm install` in the Vue project when Node/npm are present but
                frontend dependencies (vite) are missing.

        Note: The script will NOT install Node.js itself; install Node from
        https://nodejs.org if not present.

    AppData is NOT managed by this script. Copy app files manually into
    src\Automaton.Api\AppData as needed.

.PARAMETER MainRepo
    Path to the main Automaton repository.
    Auto-detected from git worktree list if not supplied.

.PARAMETER AutoInstall
    When supplied the script will attempt to auto-install missing Python
    packages (FastAPI/uvicorn) and frontend dependencies (`npm install`) if
    the corresponding package managers (`python` and `npm`) are available.
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
$RunnerProject = Join-Path $WorktreeRoot "src\Automaton.Runner"
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
        if ($AutoInstall) {
            Write-Step "Attempting to install FastAPI and uvicorn via pip..."
            try {
                python -m pip install --upgrade pip
                python -m pip install fastapi "uvicorn[standard]"
                python -c "import fastapi" 2>&1 | Out-String | Out-Null; $fastapiOk = ($LASTEXITCODE -eq 0)
                python -c "import uvicorn" 2>&1 | Out-String | Out-Null; $uvicornOk = ($LASTEXITCODE -eq 0)
                if ($fastapiOk -and $uvicornOk) { Write-Ok "FastAPI prerequisites OK (installed)" } else { Write-Warn "FastAPI install attempted but imports still fail." }
            } catch {
                Write-Warn "Auto-install of FastAPI failed: $_"
            }
        } else {
            Write-Warn "FastAPI or uvicorn not installed. App controller execution will not work."
            Write-Warn "Run once to fix:  pip install fastapi \"uvicorn[standard]\" or re-run with -AutoInstall"
        }
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

# ---------- 4. Automaton.Runner ---------------------------------------------

Write-Step "Starting Automaton.Runner..."
Start-DevWindow "Automaton Runner" $RunnerProject "dotnet watch"
Write-Ok "Runner window launched"

# ---------- 5. Vue.js frontend ----------------------------------------------

Write-Step "Starting Vue.js frontend..."
Start-DevWindow "Automaton Vue" $VueProject "npm run dev"
Write-Ok "Vue window launched"

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
Write-Host "  Frontend : http://localhost:5173         " -ForegroundColor White
Write-Host "  Website  : http://localhost:5174         " -ForegroundColor White
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""