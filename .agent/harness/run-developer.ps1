$ErrorActionPreference = "Stop"

Write-Host "Running Developer..."

$prompt = @"
Read .agent/agents/developer.md.
Read .agent/skills/studio-frontend/SKILL.md.
Read AgentRuns/current/task.md.
Read AgentRuns/current/review.md if it exists.

Implement the task and address the reviewer feedback.

Rules:
- Keep the diff small.
- Do not implement unrelated improvements.
- Do not modify backend behavior.
- Preserve existing reachable editor functionality.
- Run the verification commands from the task where possible.
- Write a concise implementation summary to AgentRuns/current/result.md.

Use PowerShell Set-Content when writing AgentRuns files.
"@

$jsonl = "AgentRuns\current\developer.jsonl"
$metrics = "AgentRuns\current\developer-metrics.json"

$model = "gpt-5.4"

Write-Host "Model: $model"

$prompt | codex exec `
    -m $model `
    --json `
    --dangerously-bypass-approvals-and-sandbox `
    --cd . `
    - `
    2>&1 | Tee-Object $jsonl

.\.agent\harness\write-agent-metrics.ps1 `
    -Role "developer" `
    -Model $model `
    -JsonlPath $jsonl `
    -MetricsPath $metrics

Write-Host ""
Write-Host "Developer result:"
Write-Host "----------------------------------------"

if (Test-Path "AgentRuns\current\result.md") {
    Get-Content AgentRuns\current\result.md
}

Write-Host ""
Write-Host "Git status:"
Write-Host "----------------------------------------"
git status --short