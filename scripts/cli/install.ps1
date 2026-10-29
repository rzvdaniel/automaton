# Automaton CLI installer for Windows PowerShell
# Usage (PowerShell):
#   irm https://your-server/install.ps1 | iex
#
# Requires the .NET 9 SDK or later. Install from https://dot.net if not already present.

$ErrorActionPreference = "Stop"

# Check for .NET
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET runtime not found." -ForegroundColor Red
    Write-Host "Install the .NET SDK (9 or later) from https://dot.net and re-run this script."
    exit 1
}

Write-Host "Installing Automaton CLI..." -ForegroundColor Cyan

try {
    dotnet tool install --global Automaton.Cli 2>&1 | Out-Null
} catch {
    dotnet tool update --global Automaton.Cli | Out-Null
}

# Ensure dotnet tools are on PATH for this session
$dotnetTools = "$env:USERPROFILE\.dotnet\tools"
if ($env:PATH -notlike "*$dotnetTools*") {
    $env:PATH = "$env:PATH;$dotnetTools"
    Write-Host ""
    Write-Host "NOTE: Add the following to your system PATH permanently:" -ForegroundColor Yellow
    Write-Host "  $dotnetTools"
}

Write-Host ""
Write-Host "Automaton CLI installed." -ForegroundColor Green
Write-Host "Run 'automaton --help' to get started."
Write-Host ""
Write-Host "Quick start:"
Write-Host "  automaton config --server https://your-automaton-server --key ak_execute_xxx"
Write-Host "  automaton init --name `"My App`""
Write-Host "  automaton deploy"
