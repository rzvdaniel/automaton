param(
    [int]$MaxIterations = 3
)

$ErrorActionPreference = "Stop"

for ($i = 1; $i -le $MaxIterations; $i++) {
    Write-Host ""
    Write-Host "========================================"
    Write-Host "Agent loop iteration $i / $MaxIterations"
    Write-Host "========================================"

    .\.agent\harness\run-developer.ps1
    .\.agent\harness\run-reviewer.ps1

    if (-not (Test-Path "AgentRuns\current\review.md")) {
        throw "Review file was not created."
    }

    $review = Get-Content AgentRuns\current\review.md -Raw

    Write-Host ""
    Write-Host "Current review:"
    Write-Host "----------------------------------------"
    Write-Host $review

    if ($review -match "decision:\s*(accept|done|stop)") {
        Write-Host ""
        Write-Host "Stopping loop because reviewer decision is accept/done/stop."
        break
    }

    if ($review -match "severity:\s*minor") {
        Write-Host ""
        Write-Host "Stopping loop because remaining issues are minor."
        break
    }
}

$metricFiles = @(
    "AgentRuns\current\orchestrator-metrics.json",
    "AgentRuns\current\developer-metrics.json",
    "AgentRuns\current\reviewer-metrics.json"
)

$items = @()

foreach ($file in $metricFiles) {
    if (Test-Path $file) {
        $items += Get-Content $file -Raw | ConvertFrom-Json
    }
}

if ($items.Count -gt 0) {
    $totalInput = ($items | Measure-Object input_tokens -Sum).Sum
    $totalCached = ($items | Measure-Object cached_input_tokens -Sum).Sum
    $totalOutput = ($items | Measure-Object output_tokens -Sum).Sum
    $totalReasoning = ($items | Measure-Object reasoning_output_tokens -Sum).Sum
    $totalTokens = ($items | Measure-Object total_tokens -Sum).Sum

    $summary = [ordered]@{
        total_input_tokens = $totalInput
        total_cached_input_tokens = $totalCached
        total_output_tokens = $totalOutput
        total_reasoning_output_tokens = $totalReasoning
        total_tokens = $totalTokens
        captured_at = (Get-Date).ToString("o")
        roles = $items
    }

    $summary | ConvertTo-Json -Depth 10 |
        Set-Content "AgentRuns\current\metrics.json"

    Write-Host ""
    Write-Host "Metrics:"
    Write-Host "----------------------------------------"
    Get-Content AgentRuns\current\metrics.json
}

Write-Host ""
Write-Host "Loop finished. Review manually:"
Write-Host "----------------------------------------"
Write-Host "git diff"
Write-Host "AgentRuns\current\review.md"