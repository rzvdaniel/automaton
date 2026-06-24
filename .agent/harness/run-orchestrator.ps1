$ErrorActionPreference = "Stop"

Write-Host "Running Orchestrator..."

$model = "gpt-5.5"
$jsonl = "AgentRuns\current\orchestrator.jsonl"
$metrics = "AgentRuns\current\orchestrator-metrics.json"

Write-Host "Model: $model"

$prompt = @"
Read AGENTS.md if it exists.
Read .agent/maps/automaton.md if it exists.
Read .agent/agents/orchestrator.md.
Read AgentRuns/current/goal.md.
Read AgentRuns/current/review.md if it exists.

Use AGENTS.md and .agent/maps/automaton.md as the repository map before searching the repo.
Prefer targeted file inspection over broad exploration.

Follow the orchestrator role instructions.

Write:
- AgentRuns/current/task.md
- AgentRuns/current/context.md

Use PowerShell Set-Content when writing AgentRuns files.
Do not modify application code.
"@

$prompt | codex exec `
    -m $model `
    --json `
    --dangerously-bypass-approvals-and-sandbox `
    --cd . `
    - `
    2>&1 | Tee-Object $jsonl

if ($LASTEXITCODE -ne 0) {
    throw "Codex orchestrator failed with exit code $LASTEXITCODE. See $jsonl"
}

$jsonlContent = Get-Content $jsonl -Raw

if ($jsonlContent -match '"type":"error"' -or $jsonlContent -match '"type":"turn.failed"') {
    throw "Codex orchestrator failed. See $jsonl"
}

.\.agent\harness\write-agent-metrics.ps1 `
    -Role "orchestrator" `
    -Model $model `
    -JsonlPath $jsonl `
    -MetricsPath $metrics

if (-not (Test-Path "AgentRuns\current\task.md")) {
    throw "Orchestrator did not create AgentRuns\current\task.md"
}

$taskContent = Get-Content "AgentRuns\current\task.md" -Raw

if ([string]::IsNullOrWhiteSpace($taskContent)) {
    throw "Orchestrator created an empty task.md"
}

if (-not (Test-Path "AgentRuns\current\context.md")) {
    throw "Orchestrator did not create AgentRuns\current\context.md"
}

$contextContent = Get-Content "AgentRuns\current\context.md" -Raw

if ([string]::IsNullOrWhiteSpace($contextContent)) {
    throw "Orchestrator created an empty context.md"
}

Write-Host ""
Write-Host "Task created:"
Write-Host "----------------------------------------"
Get-Content AgentRuns\current\task.md

Write-Host ""
Write-Host "Context created:"
Write-Host "----------------------------------------"
Get-Content AgentRuns\current\context.md

Write-Host ""
Write-Host "Metrics:"
Write-Host "----------------------------------------"

if (Test-Path $metrics) {
    Get-Content $metrics
}
