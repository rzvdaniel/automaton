$ErrorActionPreference = "Stop"

Write-Host "Running Reviewer..."

$model = "gpt-5.5"
$jsonl = "AgentRuns\current\reviewer.jsonl"
$metrics = "AgentRuns\current\reviewer-metrics.json"
$reviewPath = "AgentRuns\current\review.md"

Write-Host "Model: $model"

$prompt = @"
Read .agent/agents/reviewer.md.
Read AgentRuns/current/task.md.
Read AgentRuns/current/context.md if it exists.
Read AgentRuns/current/result.md.
Read AgentRuns/current/developer-error-summary.txt if it exists.

Review the current git diff against the task and context.

Follow the reviewer role instructions.

Important:
- If result.md is a recovery result, inspect developer-error-summary.txt and the current git diff.
- If the captured error appears related to the current diff, use decision: fix.
- If the captured error appears unrelated/pre-existing, say so clearly and do not block only for that reason.
- Use PowerShell Set-Content when writing AgentRuns files.
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

try {
    .\.agent\harness\write-agent-metrics.ps1 `
        -Role "reviewer" `
        -Model $model `
        -JsonlPath $jsonl `
        -MetricsPath $metrics
}
catch {
    Write-Host ""
    Write-Host "Warning: failed to write reviewer metrics."
    Write-Host $_
}

if (-not (Test-Path $reviewPath)) {
    throw "Reviewer did not create AgentRuns\current\review.md"
}

$reviewContent = Get-Content $reviewPath -Raw

if ([string]::IsNullOrWhiteSpace($reviewContent)) {
    throw "Reviewer created an empty review.md"
}

Write-Host ""
Write-Host "Review:"
Write-Host "----------------------------------------"
Get-Content $reviewPath

Write-Host ""
Write-Host "Metrics:"
Write-Host "----------------------------------------"

if (Test-Path $metrics) {
    Get-Content $metrics
}
else {
    Write-Host "No reviewer metrics file created."
}
