param(
    [ValidateSet("Auto", "Small", "Tiny")]
    [string]$ForcedMode = "Auto"
)

$ErrorActionPreference = "Stop"

Write-Host "Classifying agent task scope..."

$model = "gpt-5.4"
$jsonl = "AgentRuns\current\scope-classifier.jsonl"
$scopePath = "AgentRuns\current\scope.json"

Write-Host "Model: $model"
Write-Host "Forced mode: $ForcedMode"

if (-not (Test-Path "AgentRuns\current\goal.md")) {
    throw "AgentRuns\current\goal.md does not exist."
}

$goalContent = Get-Content "AgentRuns\current\goal.md" -Raw

if ([string]::IsNullOrWhiteSpace($goalContent)) {
    throw "AgentRuns\current\goal.md is empty."
}

$prompt = @"
You are a lightweight task scope classifier and simple task writer.

Read only:
- AgentRuns/current/goal.md

Do not inspect the repository.
Do not modify application code.
Do not run searches.
Do not run build commands.

Forced mode:
$ForcedMode

Decide the best mode unless Forced mode is Small or Tiny.

Modes:

normal:
- Use for ambiguous or medium/large work.
- Use when repository inspection is needed before planning.
- Use when multiple files or architecture decisions are likely.
- Flow: orchestrator + developer + reviewer.
- In normal mode, do not write task.md. The orchestrator will write it.

small:
- Use for clear, limited work.
- Use for small UI changes, labels, simple bug fixes, simple wiring.
- Flow: developer + reviewer only.
- You must write AgentRuns/current/task.md.

tiny:
- Use for very small mechanical changes.
- Examples: rename label, change icon, change copy text, remove one menu item.
- Flow: developer only, then human reviews git diff.
- You must write AgentRuns/current/task.md.

Rules:
- If Forced mode is Small, choose small.
- If Forced mode is Tiny, choose tiny.
- If Forced mode is Auto, choose the best mode.
- If the goal is vague, choose normal.
- If there is any risk of behavior change across multiple areas, choose normal.
- Prefer small over tiny when reviewer verification is useful.
- Prefer normal when unsure.

Write valid JSON to AgentRuns/current/scope.json using this shape:

{
  "mode": "normal|small|tiny",
  "max_iterations": 1|2|3,
  "confidence": "low|medium|high",
  "reason": "...",
  "requires_orchestrator": true|false,
  "writes_task_md": true|false
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

If mode is small or tiny, also write AgentRuns/current/task.md.

task.md must include:
- title
- goal
- likely files if obvious from the goal
- constraints
- acceptance criteria
- verification commands

For small/tiny task.md:
- Keep the task focused and executable.
- Do not invent repository details.
- If exact files are unknown, say "likely files unknown; developer should inspect only the minimum needed."
- Do not create context.md. context.md is only for normal orchestrator mode.

Important:
- Do not include markdown fences around scope.json.
- scope.json must be valid JSON only.
- Do not write task.md for normal mode.
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

if ($scope.confidence -notin @("low", "medium", "high")) {
    throw "Invalid confidence: $($scope.confidence)"
}

if ($null -eq $scope.requires_orchestrator) {
    throw "scope.json is missing requires_orchestrator"
}

if ($null -eq $scope.writes_task_md) {
    throw "scope.json is missing writes_task_md"
}

if ($scope.mode -eq "normal") {
    if ($scope.requires_orchestrator -ne $true) {
        throw "Normal mode must set requires_orchestrator = true"
    }

    if ($scope.writes_task_md -ne $false) {
        throw "Normal mode must set writes_task_md = false"
    }
}

if ($scope.mode -in @("small", "tiny")) {
    if ($scope.requires_orchestrator -ne $false) {
        throw "$($scope.mode) mode must set requires_orchestrator = false"
    }

    if ($scope.writes_task_md -ne $true) {
        throw "$($scope.mode) mode must set writes_task_md = true"
    }

    if (-not (Test-Path "AgentRuns\current\task.md")) {
        throw "Classifier selected $($scope.mode) but did not create task.md"
    }

    $taskContent = Get-Content "AgentRuns\current\task.md" -Raw

    if ([string]::IsNullOrWhiteSpace($taskContent)) {
        throw "Classifier selected $($scope.mode) but created an empty task.md"
    }
}

if ($scope.mode -eq "tiny" -and $maxIterations -ne 1) {
    throw "Tiny mode must use max_iterations = 1"
}

Write-Host ""
Write-Host "Scope:"
Write-Host "----------------------------------------"
Get-Content $scopePath

if (Test-Path "AgentRuns\current\task.md") {
    $taskContent = Get-Content "AgentRuns\current\task.md" -Raw

    if (-not [string]::IsNullOrWhiteSpace($taskContent)) {
        Write-Host ""
        Write-Host "Generated task:"
        Write-Host "----------------------------------------"
        Get-Content "AgentRuns\current\task.md"
    }
}