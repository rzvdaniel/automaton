$ErrorActionPreference = "Stop"

Write-Host "Running Orchestrator..."

$prompt = @"
Read .agent/agents/orchestrator.md.
Read AgentRuns/current/goal.md.
Read AgentRuns/current/review.md if it exists.

Create the next small implementation task.
Write the task to AgentRuns/current/task.md.

Use PowerShell Set-Content when writing AgentRuns files.
Do not modify application code.
"@

$jsonl = "AgentRuns\current\orchestrator.jsonl"
$metrics = "AgentRuns\current\orchestrator-metrics.json"

$model = "gpt-5.5-low"

Write-Host "Model: $model"

$prompt | codex exec `
    -m $model `
    --json `
    --dangerously-bypass-approvals-and-sandbox `
    --cd . `
    - `
    2>&1 | Tee-Object $jsonl

.\.agent\harness\write-agent-metrics.ps1 `
    -Role "orchestrator" `
    -Model $model `
    -JsonlPath $jsonl `
    -MetricsPath $metrics

Write-Host ""
Write-Host "Task created:"
Write-Host "----------------------------------------"

if (Test-Path "AgentRuns\current\task.md") {
    Get-Content AgentRuns\current\task.md
}

Write-Host ""
Write-Host "Metrics:"
Write-Host "----------------------------------------"

if (Test-Path $metrics) {
    Get-Content $metrics
}