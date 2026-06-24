$ErrorActionPreference = "Stop"

Write-Host "Classifying agent task scope..."

$model = "gpt-5.4"
$jsonl = "AgentRuns\current\scope-classifier.jsonl"
$scopePath = "AgentRuns\current\scope.json"

Write-Host "Model: $model"

$prompt = @"
You are a lightweight task scope classifier.

Read only:
- AgentRuns/current/goal.md
- AgentRuns/current/task.md if it exists

Do not inspect the repository.
Do not modify application code.
Do not run searches.
Do not run build commands.

Decide the best mode:

normal:
- Use for ambiguous or medium/large work.
- Use when repo inspection is needed before planning.
- Use when multiple files or architecture decisions are likely.
- Flow: orchestrator + developer + reviewer.

small:
- Use when the task is clear and already written in task.md.
- Use for small UI changes, labels, small bug fixes, simple wiring.
- Flow: developer + reviewer only.

tiny:
- Use for very small mechanical changes.
- Examples: rename label, change icon, change copy text, remove one menu item.
- Flow: developer only, then human reviews git diff.

Rules:
- If task.md is empty and the work is not tiny, prefer normal.
- If task.md is empty, do not choose small.
- If the goal is vague, choose normal.
- If there is any risk of behavior change across multiple areas, choose normal.
- Prefer small over tiny when verification by reviewer is useful.
- Prefer normal when unsure.

Write only valid JSON to AgentRuns/current/scope.json using this shape:

{
  "mode": "normal|small|tiny",
  "max_iterations": 1|2|3,
  "confidence": "low|medium|high",
  "reason": "...",
  "requires_task_md": true|false
}

Max iteration rules:
- 1 = tiny or very clear small task.
- 2 = normal small/medium task where one fix pass may be useful.
- 3 = larger or riskier task, or when unsure.
- Never choose more than 3.
- For tiny mode, always choose 1.
- For small mode, usually choose 1 or 2.
- For normal mode, choose 2 or 3.
- Prefer fewer iterations when the task is clear.
"@

$prompt | codex exec `
    -m $model `
    --json `
    --dangerously-bypass-approvals-and-sandbox `
    --cd . `
    - `
    2>&1 | Tee-Object $jsonl

if ($LASTEXITCODE -ne 0) {
    throw "Scope classifier failed with exit code $LASTEXITCODE. See $jsonl"
}

$jsonlContent = Get-Content $jsonl -Raw

if ($jsonlContent -match '"type":"error"' -or $jsonlContent -match '"type":"turn.failed"') {
    throw "Scope classifier failed. See $jsonl"
}

if (-not (Test-Path $scopePath)) {
    throw "Scope classifier did not create $scopePath"
}

$scopeContent = Get-Content $scopePath -Raw

if ([string]::IsNullOrWhiteSpace($scopeContent)) {
    throw "Scope classifier created an empty scope.json"
}

try {
    $scope = $scopeContent | ConvertFrom-Json
}
catch {
    throw "scope.json is not valid JSON."
}

if ($scope.mode -notin @("normal", "small", "tiny")) {
    throw "Invalid scope mode: $($scope.mode)"
}

if ($null -eq $scope.max_iterations) {
    throw "scope.json is missing max_iterations"
}

$maxIterations = [int]$scope.max_iterations

if ($maxIterations -lt 1 -or $maxIterations -gt 3) {
    throw "Invalid max_iterations: $maxIterations. Must be between 1 and 3."
}

Write-Host ""
Write-Host "Scope:"
Write-Host "----------------------------------------"
Get-Content $scopePath