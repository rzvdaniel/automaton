param(
    [int]$MaxIterations = 0,
    [switch]$SkipReviewer
)

$ErrorActionPreference = "Stop"

$runRoot = "AgentRuns\current"
$goalPath = "$runRoot\goal.md"
$taskPath = "$runRoot\task.md"
$contextPath = "$runRoot\context.md"
$scopePath = "$runRoot\scope.json"
$reviewsDir = "$runRoot\reviews"
$resultsDir = "$runRoot\results"
$decisionsPath = "$runRoot\decisions.json"

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

function Get-ReviewSection {
    param(
        [string]$Review,
        [string]$SectionName
    )

    $sectionPattern = "(?ims)^\s*$([regex]::Escape($SectionName))\s*:\s*(.*?)(?=^\s*(decision|severity|reason|next_action|follow_up_items)\s*:|\z)"
    $match = [regex]::Match($Review, $sectionPattern)

    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value.Trim()
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

function Save-IterationMetrics {
    param(
        [string]$Role,
        [int]$Iteration
    )

    $sourcePath = "$runRoot\$Role-metrics.json"
    if (-not (Test-Path $sourcePath)) {
        return
    }

    $targetPath = "$runRoot\$Role-metrics.iteration-$Iteration.json"
    Copy-Item $sourcePath $targetPath -Force
}

function Save-IterationTextArtifact {
    param(
        [string]$SourcePath,
        [string]$TargetDirectory,
        [string]$Name,
        [int]$Iteration
    )

    if (-not (Test-Path $SourcePath)) {
        return ""
    }

    New-Item -ItemType Directory -Force -Path $TargetDirectory | Out-Null
    $targetPath = Join-Path $TargetDirectory "$Name.iteration-$Iteration.md"
    Copy-Item $SourcePath $targetPath -Force
    return $targetPath
}

function Save-DecisionSnapshot {
    param(
        [int]$Iteration,
        [string]$Decision,
        [string]$Severity,
        [string]$Review,
        [string]$ReviewPath,
        [string]$ResultPath
    )

    $items = @()

    if (Test-Path $decisionsPath) {
        try {
            $existing = Get-Content $decisionsPath -Raw | ConvertFrom-Json
            if ($null -ne $existing) {
                $items += @($existing)
            }
        }
        catch {
            Write-Host "Warning: could not parse existing decisions.json. Rebuilding it from current iteration."
        }
    }

    $items = @($items | Where-Object { $_.iteration -ne $Iteration })

    $decisionItem = [ordered]@{
        iteration = $Iteration
        decision = $Decision
        severity = $Severity
        reason = Get-ReviewSection -Review $Review -SectionName "reason"
        next_action = Get-ReviewSection -Review $Review -SectionName "next_action"
        follow_up_items = Get-ReviewSection -Review $Review -SectionName "follow_up_items"
        review_path = $ReviewPath
        result_path = $ResultPath
        captured_at = (Get-Date).ToString("o")
    }

    $items += [pscustomobject]$decisionItem
    $items = @($items | Sort-Object iteration)

    $items | ConvertTo-Json -Depth 8 | Set-Content $decisionsPath
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
    Save-IterationMetrics -Role "developer" -Iteration $i
    $resultSnapshotPath = Save-IterationTextArtifact -SourcePath "$runRoot\result.md" -TargetDirectory $resultsDir -Name "result" -Iteration $i

    if ($SkipReviewer) {
        Write-Host ""
        Write-Host "Skipping reviewer. Manual git diff review required."
        $lastDecision = "manual-review"
        break
    }

    .\.agent\harness\run-reviewer.ps1
    Save-IterationMetrics -Role "reviewer" -Iteration $i
    $reviewSnapshotPath = Save-IterationTextArtifact -SourcePath "$runRoot\review.md" -TargetDirectory $reviewsDir -Name "review" -Iteration $i

    if (-not (Test-Path "$runRoot\review.md")) {
        throw "Review file was not created."
    }

    $review = Get-Content "$runRoot\review.md" -Raw
    $lastDecision = Get-ReviewField -Review $review -FieldName "decision"
    $lastSeverity = Get-ReviewField -Review $review -FieldName "severity"

    Save-DecisionSnapshot `
        -Iteration $i `
        -Decision $lastDecision `
        -Severity $lastSeverity `
        -Review $review `
        -ReviewPath $reviewSnapshotPath `
        -ResultPath $resultSnapshotPath

    Write-Host ""
    Write-Host "Current review:"
    Write-Host "----------------------------------------"
    Write-Host $review

    Write-Host ""
    Write-Host "Parsed reviewer decision: $lastDecision"
    Write-Host "Parsed reviewer severity: $lastSeverity"

    $shouldStopLoop = $false

    switch ($lastDecision) {
        "accept" {
            Write-Host ""
            Write-Host "Stopping loop because reviewer accepted the task."
            $shouldStopLoop = $true
        }

        "done" {
            Write-Host ""
            Write-Host "Stopping loop because reviewer marked the task done."
            $shouldStopLoop = $true
        }

        "stop" {
            Write-Host ""
            Write-Host "Stopping loop because reviewer requested human intervention."
            $shouldStopLoop = $true
        }

        "fix" {
            if ($i -lt $effectiveMaxIterations) {
                Write-Host ""
                Write-Host "Reviewer requested fixes. Continuing to developer iteration $($i + 1) / $effectiveMaxIterations."
            }
            else {
                Write-Host ""
                Write-Host "Reviewer requested fixes, but maximum iterations were reached. Manual review required."
                $shouldStopLoop = $true
            }
        }

        default {
            Write-Host ""
            Write-Host "Reviewer decision was missing or unknown. Stopping for manual review."
            $shouldStopLoop = $true
        }
    }

    if ($shouldStopLoop) {
        break
    }
}

$metricFiles = @(
    "$runRoot\scope-classifier-metrics.json",
    "$runRoot\orchestrator-metrics.json"
)

$iterationMetricFiles = Get-ChildItem $runRoot -Filter "*-metrics.iteration-*.json" -ErrorAction SilentlyContinue |
    Sort-Object Name |
    ForEach-Object { $_.FullName }

if ($iterationMetricFiles.Count -gt 0) {
    $metricFiles += $iterationMetricFiles
}
else {
    $metricFiles += @(
        "$runRoot\developer-metrics.json",
        "$runRoot\reviewer-metrics.json"
    )
}

$items = @()

foreach ($file in $metricFiles) {
    if (Test-Path $file) {
        $metric = Get-Content $file -Raw | ConvertFrom-Json

        if ($file -match 'iteration-(\d+)\.json$') {
            $metric | Add-Member -NotePropertyName iteration -NotePropertyValue ([int]$Matches[1]) -Force
        }

        $items += $metric
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
        Set-Content "$runRoot\metrics.json"

    Write-Host ""
    Write-Host "Metrics:"
    Write-Host "----------------------------------------"
    Get-Content "$runRoot\metrics.json"
}

Write-Host ""
Write-Host "Loop finished. Review manually:"
Write-Host "----------------------------------------"
Write-Host "Last reviewer decision: $lastDecision"
Write-Host "Last reviewer severity: $lastSeverity"
Write-Host "git diff"
Write-Host "$runRoot\result.md"
Write-Host "$runRoot\review.md"

if (Test-Path $decisionsPath) {
    Write-Host $decisionsPath
}
