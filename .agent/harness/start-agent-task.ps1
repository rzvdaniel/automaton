param(
    [int]$MaxIterations = 3
)

$ErrorActionPreference = "Stop"

Write-Host "Starting new agent task..."

Write-Host "Checking git status..."
$gitStatus = git status --short

if ($gitStatus) {
    Write-Host ""
    Write-Host "WARNING: You have uncommitted changes:"
    Write-Host $gitStatus
    Write-Host ""
    $continue = Read-Host "Continue anyway? (y/N)"

    if ($continue -ne "y" -and $continue -ne "Y") {
        Write-Host "Aborted."
        exit 1
    }
}

Write-Host "Clearing previous agent run files..."

New-Item -ItemType Directory -Force -Path "AgentRuns\current" | Out-Null

Set-Content "AgentRuns\current\task.md" ""
Set-Content "AgentRuns\current\result.md" ""
Set-Content "AgentRuns\current\review.md" ""

Remove-Item "AgentRuns\current\*.jsonl" -ErrorAction SilentlyContinue
Remove-Item "AgentRuns\current\*-metrics.json" -ErrorAction SilentlyContinue
Remove-Item "AgentRuns\current\metrics.json" -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Make sure your goal is written here:"
Write-Host "AgentRuns\current\goal.md"
Write-Host ""

$run = Read-Host "Run orchestrator and agent loop now? (y/N)"

if ($run -ne "y" -and $run -ne "Y") {
    Write-Host "Prepared run folder. Edit goal.md, then run:"
    Write-Host ".\.agent\harness\run-orchestrator.ps1"
    Write-Host ".\.agent\harness\run-agent-loop.ps1 -MaxIterations $MaxIterations"
    exit 0
}

.\.agent\harness\run-orchestrator.ps1
.\.agent\harness\run-agent-loop.ps1 -MaxIterations $MaxIterations

Write-Host ""
Write-Host "Done. Review:"
Write-Host "git diff"
Write-Host "AgentRuns\current\review.md"