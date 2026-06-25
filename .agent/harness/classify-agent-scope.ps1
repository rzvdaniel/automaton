param(
    [ValidateSet("Auto", "Micro", "Small", "Tiny")]
    [string]$ForcedMode = "Auto"
)

$ErrorActionPreference = "Stop"

Write-Host "Classifying agent task scope..."

$model = "gpt-5.4"
$jsonl = "AgentRuns\current\scope-classifier.jsonl"
$scopePath = "AgentRuns\current\scope.json"
$metrics = "AgentRuns\current\scope-classifier-metrics.json"

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

Decide the best mode unless Forced mode is Micro, Small, or Tiny.

Modes:

normal:
- Use for ambiguous or medium/large work.
- Use when repository inspection is needed before planning.
- Use when multiple files or architecture decisions are likely.
- Flow: orchestrator + developer + reviewer.
- In normal mode, do not write task.md. The orchestrator will write it.

small:
- Use for clear, limited work that still benefits from reviewer validation.
- Use for small bug fixes, small wiring changes, or small UI behavior changes.
- Flow: developer + reviewer only.
- You must write AgentRuns/current/task.md.

tiny:
- Use for very small mechanical changes where human review of git diff is enough.
- Examples: change copy text, change icon, remove one menu item, simple config tweak.
- Flow: developer only, then human reviews git diff.
- You must write AgentRuns/current/task.md.

micro:
- Use for the cheapest possible safe path.
- Use when the task is likely one obvious file and a label/default/state/CSS tweak.
- Examples: rename a visible label, change a default boolean, change a default splitter ratio, tiny CSS spacing tweak, one-line config value.
- Do not use when architecture, cross-file behavior, data changes, security, backend behavior, or test changes are likely.
- Flow: developer only, no reviewer, no orchestrator.
- You must write AgentRuns/current/task.md.

Rules:
- If Forced mode is Micro, choose micro.
- If Forced mode is Small, choose small.
- If Forced mode is Tiny, choose tiny.
- If Forced mode is Auto, choose the best mode.
- If the goal is vague, choose normal.
- If there is any risk of behavior change across multiple areas, choose normal.
- Prefer micro over tiny only when the change is very likely one obvious file and does not require reviewer judgment.
- Prefer small over tiny/micro when reviewer verification is useful.
- Prefer normal when unsure.

Write valid JSON to AgentRuns/current/scope.json using this shape:

{
  "mode": "normal|small|tiny|micro",
  "max_iterations": 1|2|3,
  "confidence": "low|medium|high",
  "reason": "...",
  "requires_orchestrator": true|false,
  "writes_task_md": true|false,
  "requires_reviewer": true|false,
  "token_budget": {
    "developer_input_max": 60000,
    "reviewer_input_max": 0,
    "max_files_to_inspect": 3
  }
}

Token budget rules:
- micro: developer_input_max 60000, reviewer_input_max 0, max_files_to_inspect 3.
- tiny: developer_input_max 80000, reviewer_input_max 0, max_files_to_inspect 5.
- small: developer_input_max 120000, reviewer_input_max 40000, max_files_to_inspect 8.
- normal: developer_input_max 300000, reviewer_input_max 80000, max_files_to_inspect 0.

Max iteration rules:
- 1 = micro, tiny, or very clear small task.
- 2 = normal small/medium task where one fix pass may be useful.
- 3 = larger or riskier task, or when unsure.
- Never choose more than 3.
- For micro and tiny mode, always choose 1.
- For small mode, usually choose 1 or 2.
- For normal mode, choose 2 or 3.
- Prefer fewer iterations when the task is clear.

If mode is micro, small, or tiny, also write AgentRuns/current/task.md.

task.md must include:
- title
- goal
- likely files if obvious from the goal
- constraints
- acceptance criteria
- verification commands
- token budget guidance

For micro/small/tiny task.md:
- Keep the task focused and executable.
- Do not invent repository details.
- If exact files are unknown, say "likely files unknown; developer should inspect only the minimum needed."
- For micro tasks, explicitly say the developer should search first and inspect no more than 3 files unless unavoidable.
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

try {
    .\.agent\harness\write-agent-metrics.ps1 `
        -Role "scope-classifier" `
        -Model $model `
        -JsonlPath $jsonl `
        -MetricsPath $metrics
}
catch {
    Write-Host ""
    Write-Host "Warning: failed to write scope classifier metrics."
    Write-Host $_
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

if ($scope.mode -notin @("normal", "small", "tiny", "micro")) {
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

if ($null -eq $scope.requires_reviewer) {
    throw "scope.json is missing requires_reviewer"
}

if ($null -eq $scope.token_budget) {
    throw "scope.json is missing token_budget"
}

if ($scope.mode -eq "normal") {
    if ($scope.requires_orchestrator -ne $true) {
        throw "Normal mode must set requires_orchestrator = true"
    }

    if ($scope.writes_task_md -ne $false) {
        throw "Normal mode must set writes_task_md = false"
    }
}

if ($scope.mode -in @("micro", "small", "tiny")) {
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

if ($scope.mode -in @("micro", "tiny") -and $maxIterations -ne 1) {
    throw "$($scope.mode) mode must use max_iterations = 1"
}

if ($scope.mode -in @("micro", "tiny") -and $scope.requires_reviewer -ne $false) {
    throw "$($scope.mode) mode must set requires_reviewer = false"
}

if ($scope.mode -eq "small" -and $scope.requires_reviewer -ne $true) {
    throw "Small mode must set requires_reviewer = true"
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
