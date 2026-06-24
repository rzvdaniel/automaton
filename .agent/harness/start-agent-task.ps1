param(
    [int]$MaxIterations = 3,
    [ValidateSet("Auto", "Normal", "Small", "Tiny")]
    [string]$Mode = "Auto"
)

$ErrorActionPreference = "Stop"

Write-Host "Starting agent task..."
Write-Host "Requested mode: $Mode"

Write-Host ""
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

New-Item -ItemType Directory -Force -Path "AgentRuns\current" | Out-Null

Write-Host ""
Write-Host "Clearing previous agent run files..."

Set-Content "AgentRuns\current\result.md" ""
Set-Content "AgentRuns\current\review.md" ""

Remove-Item "AgentRuns\current\*.jsonl" -ErrorAction SilentlyContinue
Remove-Item "AgentRuns\current\*-metrics.json" -ErrorAction SilentlyContinue
Remove-Item "AgentRuns\current\metrics.json" -ErrorAction SilentlyContinue
Remove-Item "AgentRuns\current\scope.json" -ErrorAction SilentlyContinue
Remove-Item "AgentRuns\current\context.md" -ErrorAction SilentlyContinue

# Do not automatically clear goal.md.
# Do not automatically clear task.md, because Small/Tiny mode may rely on a manually written task.

if (-not (Test-Path "AgentRuns\current\goal.md")) {
    Set-Content "AgentRuns\current\goal.md" ""
}

if (-not (Test-Path "AgentRuns\current\task.md")) {
    Set-Content "AgentRuns\current\task.md" ""
}

$goalContent = Get-Content "AgentRuns\current\goal.md" -Raw
$taskContent = Get-Content "AgentRuns\current\task.md" -Raw

if ([string]::IsNullOrWhiteSpace($goalContent) -and [string]::IsNullOrWhiteSpace($taskContent)) {
    Write-Host ""
    Write-Host "No goal.md or task.md content found."
    Write-Host "Write your goal here:"
    Write-Host "AgentRuns\current\goal.md"
    Write-Host ""
    Write-Host "Or write a direct task here for Small/Tiny mode:"
    Write-Host "AgentRuns\current\task.md"
    exit 1
}

$effectiveMode = $Mode.ToLowerInvariant()

if ($effectiveMode -eq "auto") {
    .\.agent\harness\classify-agent-scope.ps1

    $scope = Get-Content "AgentRuns\current\scope.json" -Raw | ConvertFrom-Json
    $effectiveMode = $scope.mode.ToLowerInvariant()

    $classifiedMaxIterations = [int]$scope.max_iterations

    if ($classifiedMaxIterations -lt 1) {
        $classifiedMaxIterations = 1
    }

    if ($classifiedMaxIterations -gt 3) {
        $classifiedMaxIterations = 3
    }

    $MaxIterations = $classifiedMaxIterations

    Write-Host ""
    Write-Host "Auto-selected mode: $effectiveMode"
    Write-Host "Auto-selected max iterations: $MaxIterations"
    Write-Host "Reason: $($scope.reason)"
}

Write-Host ""
Write-Host "Running mode: $effectiveMode"
Write-Host "----------------------------------------"

switch ($effectiveMode) {
    "normal" {
        # In normal mode, the orchestrator creates/replaces task.md from goal.md.
        .\.agent\harness\run-orchestrator.ps1
        .\.agent\harness\run-agent-loop.ps1 -MaxIterations $MaxIterations
    }

    "small" {
        $taskContent = Get-Content "AgentRuns\current\task.md" -Raw

        if ([string]::IsNullOrWhiteSpace($taskContent)) {
            throw "Small mode requires a manually written AgentRuns\current\task.md"
        }

        .\.agent\harness\run-agent-loop.ps1 -MaxIterations $MaxIterations
    }

    "tiny" {
        $taskContent = Get-Content "AgentRuns\current\task.md" -Raw

        if ([string]::IsNullOrWhiteSpace($taskContent)) {
            throw "Tiny mode requires a manually written AgentRuns\current\task.md"
        }

        .\.agent\harness\run-agent-loop.ps1 -MaxIterations 1 -SkipReviewer
    }

    default {
        throw "Unsupported mode: $effectiveMode"
    }
}

Write-Host ""
Write-Host "Done. Review manually:"
Write-Host "----------------------------------------"
Write-Host "git diff"
Write-Host "AgentRuns\current\result.md"

if (Test-Path "AgentRuns\current\review.md") {
    Write-Host "AgentRuns\current\review.md"
}

if (Test-Path "AgentRuns\current\scope.json") {
    Write-Host "AgentRuns\current\scope.json"
}