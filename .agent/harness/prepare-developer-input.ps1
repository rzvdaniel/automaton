$ErrorActionPreference = "Stop"

$developerInputPath = "AgentRuns\current\developer-input.md"

$task = ""
$context = ""
$review = ""

if (Test-Path "AgentRuns\current\task.md") {
    $task = Get-Content "AgentRuns\current\task.md" -Raw
}

if (Test-Path "AgentRuns\current\context.md") {
    $context = Get-Content "AgentRuns\current\context.md" -Raw
}

if (Test-Path "AgentRuns\current\review.md") {
    $review = Get-Content "AgentRuns\current\review.md" -Raw
}

$content = @"
# Developer Input

## Task

$task

## Context

$context

## Reviewer Feedback

$review
"@

Set-Content $developerInputPath $content

Write-Host "Prepared developer input:"
Write-Host $developerInputPath