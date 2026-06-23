$ErrorActionPreference = "Stop"

Write-Host "Running Reviewer..."

$model = "gpt-5.5"
$jsonl = "AgentRuns\current\reviewer.jsonl"
$metrics = "AgentRuns\current\reviewer-metrics.json"

Write-Host "Model: $model"

$prompt = @"
Read .agent/agents/reviewer.md.
Read AgentRuns/current/task.md.
Read AgentRuns/current/result.md.

Review the current git diff.

Decide whether the implementation should be accepted, fixed, stopped, or marked done.

Important:
- Consider npm type-check passing as strong evidence.
- Consider npm build failure only if the failure is related to the current diff.
- If build failed because of unrelated pre-existing errors, mention that clearly.

Write the review to AgentRuns/current/review.md.

Use PowerShell Set-Content when writing AgentRuns files.
"@

$prompt | codex exec `
    -m $model `
    --json `
    --dangerously-bypass-approvals-and-sandbox `
    --cd . `
    - `
    2>&1 | Tee-Object $jsonl

if ($LASTEXITCODE -ne 0) {
    throw "Codex reviewer failed with exit code $LASTEXITCODE. See $jsonl"
}

$jsonlContent = Get-Content $jsonl -Raw

if ($jsonlContent -match '"type":"error"' -or $jsonlContent -match '"type":"turn.failed"') {
    throw "Codex reviewer failed. See $jsonl"
}

.\.agent\harness\write-agent-metrics.ps1 `
    -Role "reviewer" `
    -Model $model `
    -JsonlPath $jsonl `
    -MetricsPath $metrics

if (-not (Test-Path "AgentRuns\current\review.md")) {
    throw "Reviewer did not create AgentRuns\current\review.md"
}

$reviewContent = Get-Content "AgentRuns\current\review.md" -Raw

if ([string]::IsNullOrWhiteSpace($reviewContent)) {
    throw "Reviewer created an empty review.md"
}

Write-Host ""
Write-Host "Review:"
Write-Host "----------------------------------------"
Get-Content AgentRuns\current\review.md

Write-Host ""
Write-Host "Metrics:"
Write-Host "----------------------------------------"

if (Test-Path $metrics) {
    Get-Content $metrics
}