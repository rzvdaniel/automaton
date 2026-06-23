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

$prompt | codex exec --dangerously-bypass-approvals-and-sandbox --cd . -

Write-Host ""
Write-Host "Task created:"
Write-Host "----------------------------------------"

if (Test-Path "AgentRuns\current\task.md") {
    Get-Content AgentRuns\current\task.md
}