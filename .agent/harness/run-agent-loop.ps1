param(
    [int]$MaxIterations = 0,
    [switch]$SkipReviewer
)

$ErrorActionPreference = "Stop"

$goalPath = "AgentRuns\current\goal.md"
$taskPath = "AgentRuns\current\task.md"
$contextPath = "AgentRuns\current\context.md"
$scopePath = "AgentRuns\current\scope.json"

function Get-ReviewField {
    param(
        [string]$Review,
        [string]$FieldName
    )

    $match = [regex]::Match(
        $Review,
        "(?im)^\s*$([regex]::Escape($FieldName))\s*:\s*([a-zA-Z_-]+)\s*$")

    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value.ToLowerInvariant()
}

function Get-EffectiveMaxIterations {
    param(
        [int]$RequestedMaxIterations
    )

    if ($RequestedMaxIterations -gt 0) {
        return $RequestedMaxIterations
    }

    if (Test-Path $scopePath) {
        try {
            $scope = Get-Content $scopePath -Raw | ConvertFrom-Json
            $scopeMaxIterations = [int]$scope.max_iterations

            if ($scopeMaxIterations -ge 1) {
                return [Math]::Min($scopeMaxIterations, 3)
            }
        }
        catch {
            Write-Host "Warning: could not read max_iterations from scope.json. Falling back to 3."
        }
    }

    return 3
}

if (Test-Path $goalPath) {
    $goalLastWrite = (Get-Item $goalPath).LastWriteTimeUtc

    $taskIsMissingOrOlder =
        -not (Test-Path $taskPath) `
        -or ((Get-Item $taskPath).LastWriteTimeUtc -lt $goalLastWrite)

    $contextIsOlder =
        (Test-Path $contextPath) `
        -and ((Get-Item $contextPath).LastWriteTimeUtc -lt $goalLastWrite)

    if ($taskIsMissingOrOlder -or $contextIsOlder) {
        throw @"
AgentRuns/current/goal.md is newer than the generated task/context files.

This looks like a new goal, but run-agent-loop.ps1 only continues an existing generated task.

Run this instead:

.\.agent\harness\start-agent-task.ps1

That will clear stale generated files and regenerate task.md/context.md from goal.md.
"@
    }
}

$effectiveMaxIterations = Get-EffectiveMaxIterations -RequestedMaxIterations $MaxIterations
$lastDecision = ""
$lastSeverity = ""

Write-Host ""
Write-Host "Agent loop max iterations: $effectiveMaxIterations"

for ($i = 1; $i -le $effectiveMaxIterations; $i++) {
    Write-Host ""
    Write-Host "========================================"
    Write-Host "Agent loop iteration $i / $effectiveMaxIterations"
    Write-Host "========================================"

    .\.agent\harness\run-developer.ps1

    if ($SkipReviewer) {
        Write-Host ""
        Write-Host "Skipping reviewer. Manual git diff review required."
        $lastDecision = "manual-review"
        break
    }

    .\.agent\harness\run-reviewer.ps1

    if (-not (Test-Path "AgentRuns\current\review.md")) {
        throw "Review file was not created."
    }

    $review = Get-Content AgentRuns\current\review.md -Raw
    $lastDecision = Get-ReviewField -Review $review -FieldName "decision"
    $lastSeverity = Get-ReviewField -Review $review -FieldName "severity"

    Write-Host ""
    Write-Host "Current review:"
    Write-Host "----------------------------------------"
    Write-Host $review

    Write-Host ""
    Write-Host "Parsed reviewer decision: $lastDecision"
    Write-Host "Parsed reviewer severity: $lastSeverity"

    switch ($lastDecision) {
        "accept" {
            Write-Host ""
            Write-Host "Stopping loop because reviewer accepted the task."
            break
        }

        "done" {
            Write-Host ""
            Write-Host "Stopping loop because reviewer marked the task done."
            break
        }

        "stop" {
            Write-Host ""
            Write-Host "Stopping loop because reviewer requested human intervention."
            break
        }

        "fix" {
            if ($i -lt $effectiveMaxIterations) {
                Write-Host ""
                Write-Host "Reviewer requested fixes. Continuing to developer iteration $($i + 1) / $effectiveMaxIterations."
                continue
            }

            Write-Host ""
            Write-Host "Reviewer requested fixes, but maximum iterations were reached. Manual review required."
            break
        }

        default {
            Write-Host ""
            Write-Host "Reviewer decision was missing or unknown. Stopping for manual review."
            break
        }
    }
}

$metricFiles = @(
    "AgentRuns\current\scope-classifier-metrics.json",
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
Write-Host "Last reviewer decision: $lastDecision"
Write-Host "Last reviewer severity: $lastSeverity"
Write-Host "git diff"
Write-Host "AgentRuns\current\result.md"
Write-Host "AgentRuns\current\review.md"
