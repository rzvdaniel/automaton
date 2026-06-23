$ErrorActionPreference = "Stop"

Write-Host "Running Reviewer..."

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

$prompt | codex exec --dangerously-bypass-approvals-and-sandbox --cd . -

Write-Host ""
Write-Host "Review:"
Write-Host "----------------------------------------"

if (Test-Path "AgentRuns\current\review.md") {
    Get-Content AgentRuns\current\review.md
}