$ErrorActionPreference = "Stop"

Write-Host "Running Developer..."

$model = "gpt-5.4"
$jsonl = "AgentRuns\current\developer.jsonl"
$metrics = "AgentRuns\current\developer-metrics.json"

Write-Host "Model: $model"

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

$prompt | codex exec `
    -m $model `
    --json `
    --dangerously-bypass-approvals-and-sandbox `
    --cd . `
    - `
    2>&1 | Tee-Object $jsonl

if ($LASTEXITCODE -ne 0) {
    throw "Codex developer failed with exit code $LASTEXITCODE. See $jsonl"
}

$jsonlContent = Get-Content $jsonl -Raw

if ($jsonlContent -match '"type":"error"' -or $jsonlContent -match '"type":"turn.failed"') {
    throw "Codex developer failed. See $jsonl"
}

.\.agent\harness\write-agent-metrics.ps1 `
    -Role "developer" `
    -Model $model `
    -JsonlPath $jsonl `
    -MetricsPath $metrics

if (-not (Test-Path "AgentRuns\current\result.md")) {
    throw "Developer did not create AgentRuns\current\result.md"
}

$resultContent = Get-Content "AgentRuns\current\result.md" -Raw

if ([string]::IsNullOrWhiteSpace($resultContent)) {
    throw "Developer created an empty result.md"
}

Write-Host ""
Write-Host "Developer result:"
Write-Host "----------------------------------------"
Get-Content AgentRuns\current\result.md

Write-Host ""
Write-Host "Git status:"
Write-Host "----------------------------------------"
git status --short

Write-Host ""
Write-Host "Metrics:"
Write-Host "----------------------------------------"

if (Test-Path $metrics) {
    Get-Content $metrics
}