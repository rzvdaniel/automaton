<#
.SYNOPSIS
    Start the Automaton worktree dev environment.

.DESCRIPTION
    1. Symlinks AppData from the main repo into this worktree's server project
    2. Starts Automaton.Studio.Server   (new terminal window)
    3. Starts Automaton.Runner          (new terminal window)
    4. Starts the Vue.js frontend       (new terminal window)

.PARAMETER MainRepo
    Path to the main Automaton repository.
    Defaults to the git worktree list's first (main) entry, auto-detected.

.EXAMPLE
    .\scripts\dev-start.ps1
    .\scripts\dev-start.ps1 -MainRepo "C:\Work\automaton"
#>

param(
    [string]$MainRepo = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Resolve paths ────────────────────────────────────────────────────────────

$WorktreeRoot  = Split-Path $PSScriptRoot -Parent
$ServerProject = Join-Path $WorktreeRoot "src\Automaton.Studio.Server"
$RunnerProject = Join-Path $WorktreeRoot "src\Automaton.Runner"
$VueProject    = Join-Path $WorktreeRoot "src\automaton-studio-vue"

# Auto-detect main repo from git worktree list if not supplied
if (-not $MainRepo) {
    $worktrees = git -C $WorktreeRoot worktree list 2>$null
    # First entry is always the main worktree
    $mainLine = ($worktrees | Select-Object -First 1) -replace '\s+\w+\s+\[.*\]', ''
    $MainRepo = $mainLine.Trim()
}

$AppDataSource = Join-Path $MainRepo "src\Automaton.Studio.Server\AppData"
$AppDataLink   = Join-Path $ServerProject "AppData"

# ── Helpers ──────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) { Write-Host "[>] $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "[✓] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "[!] $msg" -ForegroundColor Yellow }

function Start-DevWindow([string]$title, [string]$workDir, [string]$command) {
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "
        `$host.UI.RawUI.WindowTitle = '$title'
        Set-Location '$workDir'
        $command
    "
}

# ── 1. AppData symlink ────────────────────────────────────────────────────────

Write-Step "Setting up AppData symlink..."

if (-not (Test-Path $AppDataSource)) {
    Write-Warn "AppData source not found at: $AppDataSource"
    Write-Warn "The server will start without app data. Create some apps in the main repo first."
} else {
    # Remove existing folder or stale symlink
    if (Test-Path $AppDataLink) {
        $item = Get-Item $AppDataLink -Force
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            Write-Step "Removing existing symlink..."
            (Get-Item $AppDataLink).Delete()
        } else {
            Write-Step "Removing existing AppData folder..."
            Remove-Item $AppDataLink -Recurse -Force
        }
    }

    New-Item -ItemType SymbolicLink -Path $AppDataLink -Target $AppDataSource | Out-Null
    Write-Ok "AppData symlinked: $AppDataSource -> $AppDataLink"
}

# ── 2. Automaton.Studio.Server ───────────────────────────────────────────────

Write-Step "Starting Automaton.Studio.Server..."
Start-DevWindow "Automaton Server" $ServerProject "dotnet run"
Write-Ok "Server window launched"

# Give the server a moment to bind its port before the runner tries to connect
Start-Sleep -Seconds 2

# ── 3. Automaton.Runner ──────────────────────────────────────────────────────

Write-Step "Starting Automaton.Runner..."
Start-DevWindow "Automaton Runner" $RunnerProject "dotnet run"
Write-Ok "Runner window launched"

# ── 4. Vue.js frontend ───────────────────────────────────────────────────────

Write-Step "Starting Vue.js frontend..."
Start-DevWindow "Automaton Vue" $VueProject "npm run dev"
Write-Ok "Vue window launched"

# ── Summary ──────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Automaton dev environment starting up     " -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Server   → https://localhost:7091         " -ForegroundColor White
Write-Host "  Swagger  → https://localhost:7091/swagger " -ForegroundColor White
Write-Host "  Frontend → http://localhost:5173          " -ForegroundColor White
Write-Host "════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
