# Agent Harness Architecture

## Goal

The Automaton Agent Harness provides a structured workflow around Codex to perform incremental software development with specialized AI roles.

Its objectives are to:

* Minimize token usage
* Reduce unnecessary repository exploration
* Support iterative development
* Recover gracefully from AI failures
* Produce reproducible execution
* Keep humans in control

---

# Design Principles

## 1. Single Responsibility

Each agent has one clear responsibility.

* Scope Classifier chooses the execution strategy and token budget.
* Orchestrator plans larger work.
* Developer modifies code.
* Reviewer validates the implementation when the selected mode requires review.
* The loop script owns iteration control.

No role should perform another role's responsibilities.

## 2. Recoverability

AI failures should never lose useful work. If code changes already exist, the harness should generate recovery files instead of aborting.

## 3. Determinism

Every execution produces a reproducible trail. Generated files describe exactly what happened and allow another agent—or a human—to continue from the current state.

## 4. Minimal Context

Every role should receive only the information it needs. Large repository exploration should happen once. Subsequent roles should reuse prepared context rather than rediscovering it.

## 5. Human Override

Every stage can be inspected, edited, or rerun manually. The harness assists the user but never removes control.

---

# Overall Flow

```text
goal.md
    │
    ▼
Scope Classifier
    │
    ├── Micro
    │       ▼
    │   Developer
    │       ▼
    │   Human git diff review
    │
    ├── Tiny
    │       ▼
    │   Developer
    │       ▼
    │   Human git diff review
    │
    ├── Small
    │       ▼
    │   Developer
    │       ▼
    │   Reviewer
    │       │
    │       ├── accept/done ───► finish
    │       ├── stop ──────────► human intervention
    │       └── fix ───────────► Developer, if iterations remain
    │
    └── Normal
            ▼
      Orchestrator
            │
     task.md + context.md
            │
            ▼
        Developer
            │
        result.md
            │
            ▼
        Reviewer
            │
            ├── accept/done ───► finish
            ├── stop ──────────► human intervention
            └── fix ───────────► Developer, if iterations remain
```

The loop continues until:

* reviewer returns `accept`
* reviewer returns `done`
* reviewer returns `stop`
* reviewer returns an unknown or missing decision
* maximum iterations are reached
* reviewer is skipped because the selected mode is developer-only

---

# Execution Modes

## Micro

Input:

* `goal.md`

Classifier generates:

* `scope.json`
* `task.md`

Runs:

* Developer only

Used for the cheapest safe path: one obvious file, mechanical UI/copy/default-value/CSS changes, and no reviewer judgment required.

Examples:

* rename a visible label
* change a default boolean
* change a default splitter ratio
* tiny CSS spacing tweak
* one-line config value

Manual `git diff` review is expected.

Default budget:

```json
{
  "developer_input_max": 60000,
  "reviewer_input_max": 0,
  "max_files_to_inspect": 3
}
```

## Tiny

Input:

* `goal.md`

Classifier generates:

* `scope.json`
* `task.md`

Runs:

* Developer only

Used for trivial mechanical changes where human review is expected.

Default budget:

```json
{
  "developer_input_max": 80000,
  "reviewer_input_max": 0,
  "max_files_to_inspect": 5
}
```

## Small

Input:

* `goal.md`

Classifier generates:

* `scope.json`
* `task.md`

Runs:

* Developer
* Reviewer

No orchestrator is required. Used for clear, limited work that benefits from reviewer validation.

If the reviewer returns `fix`, `run-agent-loop.ps1` runs another developer/reviewer iteration until the task is accepted, stopped, or `max_iterations` is reached.

Default budget:

```json
{
  "developer_input_max": 120000,
  "reviewer_input_max": 40000,
  "max_files_to_inspect": 8
}
```

## Normal

Runs:

1. Scope Classifier
2. Orchestrator
3. Developer
4. Reviewer
5. Developer/Reviewer fix iterations when needed

Used for ambiguous, larger, cross-project, or architecture-sensitive work.

The orchestrator runs once. It is not re-run after reviewer feedback. Follow-up developer passes receive the existing task/context plus the latest review.

Default budget:

```json
{
  "developer_input_max": 300000,
  "reviewer_input_max": 80000,
  "max_files_to_inspect": 0
}
```

---

# Reviewer Decision State Machine

`run-agent-loop.ps1` parses `AgentRuns/current/review.md` after every reviewer pass.

Expected format:

```text
decision: accept | fix | done | stop
severity: none | minor | major | critical
```

Decision handling:

```text
accept  -> finish successfully
done    -> finish successfully
stop    -> stop for human intervention
fix     -> run another developer/reviewer iteration if iterations remain
unknown -> stop for human review
missing -> stop for human review
```

When `decision: fix` is returned and iterations remain, the loop prints the next iteration number and invokes the developer again.

When `decision: fix` is returned on the last allowed iteration, the loop stops and reports that manual review is required.

---

# Iteration Ownership

`start-agent-task.ps1` starts a new task from `goal.md` and delegates to the appropriate workflow.

`run-agent-loop.ps1` owns developer/reviewer iteration after planning has been generated.

The classifier writes `max_iterations` to `scope.json`. If `run-agent-loop.ps1` is called without `-MaxIterations`, it reads `scope.json` and uses that value. The loop caps automatic iterations at 3.

The orchestrator runs only once per normal task. It should not be re-run for normal reviewer fixes unless the human edits `goal.md` or intentionally restarts the task.

---

# Responsibilities

## Scope Classifier

Purpose:

Determine:

* execution mode
* implementation complexity
* recommended maximum iterations
* reviewer requirement
* token budget

Produces:

```text
scope.json
```

Micro/Tiny/Small modes also generate:

```text
task.md
```

`scope.json` includes:

```json
{
  "mode": "normal|small|tiny|micro",
  "max_iterations": 1,
  "confidence": "low|medium|high",
  "reason": "...",
  "requires_orchestrator": false,
  "writes_task_md": true,
  "requires_reviewer": false,
  "token_budget": {
    "developer_input_max": 60000,
    "reviewer_input_max": 0,
    "max_files_to_inspect": 3
  }
}
```

## Orchestrator

Purpose:

Convert a user goal into a precise implementation task for normal mode.

Reads:

```text
goal.md
review.md (optional)
.agent/maps/automaton.md
.agent/agents/orchestrator.md
```

Produces:

```text
task.md
context.md
```

Does **not** modify application code.

Responsibilities:

* identify relevant files
* identify relevant components
* reduce repository exploration
* prepare developer context

## Developer

Purpose:

Implement code.

Reads:

```text
developer-input.md
```

Generated from:

```text
scope.json
task.md
context.md, when available
review.md, when available
```

Produces:

```text
result.md
```

Developer is the **only** role allowed to modify code.

For micro/tiny/small modes, the developer must follow the file-inspection budget in `developer-input.md` and search before opening files when exact files are unknown.

For reviewer fix iterations, the developer should treat `review.md` as the immediate source of truth for the next pass while preserving the original task constraints.

If Codex crashes after modifying files, the harness generates:

```text
result.md
developer-error-summary.txt
```

allowing reviewer or human review to continue.

## Reviewer

Purpose:

Validate the implementation when the selected mode requires reviewer validation.

Reads:

```text
review-input.md
```

Generated from:

```text
scope.json
task.md
context.md
result.md
git status
git diff --stat
capped git diff
developer-error-summary.txt
```

Produces:

```text
review.md
```

Possible decisions:

```text
accept
fix
done
stop
```

Reviewer should not inspect repository files unless the prepared diff is truncated, incomplete, or insufficient to evaluate acceptance criteria.

---

# Task Lifecycle

A new task always starts from:

```text
AgentRuns/current/goal.md
```

The user edits **only** `goal.md`.

## New Goal

Execute:

```powershell
.\.agent\harness\start-agent-task.ps1
```

This script:

* clears generated run files
* classifies the goal
* generates planning artifacts
* starts the appropriate workflow
* runs the developer/reviewer loop automatically when the selected mode requires it

Modes can be forced:

```powershell
.\.agent\harness\start-agent-task.ps1 -Mode Micro
.\.agent\harness\start-agent-task.ps1 -Mode Tiny
.\.agent\harness\start-agent-task.ps1 -Mode Small
.\.agent\harness\start-agent-task.ps1 -Mode Normal
```

## Continue Existing Task

Execute:

```powershell
.\.agent\harness\run-agent-loop.ps1
```

Use this when:

* a task already has valid `task.md` and optional `context.md`
* the previous execution was interrupted
* a harness bug was fixed and the same task should continue
* a reviewer returned `fix` but the task was not continued automatically

Do **not** use it immediately after editing `goal.md`. In that case, use `start-agent-task.ps1` so stale planning is cleared and regenerated.

## Individual Roles

Individual role scripts are for debugging or manual override:

```powershell
.\.agent\harness\run-orchestrator.ps1
.\.agent\harness\run-developer.ps1
.\.agent\harness\run-reviewer.ps1
```

Do not use individual role scripts as the normal workflow.

---

# Stale Task Protection

The harness prevents stale planning.

`run-agent-loop.ps1` checks:

```text
goal.md
vs
task.md
context.md
```

If `goal.md` is newer, the loop stops and instructs the user to execute:

```powershell
.\.agent\harness\start-agent-task.ps1
```

preventing developers from working on obsolete tasks.

---

# AgentRuns/current

## User-authored

```text
goal.md
```

## Planning

```text
scope.json
task.md
context.md
```

## Developer

```text
developer-input.md
result.md
developer-error-summary.txt
```

## Reviewer

```text
review-input.md
review.md
reviewer-error-summary.txt
```

## Metrics

```text
scope-classifier-metrics.json
orchestrator-metrics.json
developer-metrics.json
reviewer-metrics.json
developer-metrics.iteration-1.json
reviewer-metrics.iteration-1.json
developer-metrics.iteration-2.json
reviewer-metrics.iteration-2.json
metrics.json
```

The non-iteration developer/reviewer metrics files represent the latest role run. Iteration snapshot files preserve each pass for aggregation.

## Raw Logs

```text
*.jsonl
```

These contain the raw Codex execution stream.

---

# Repository Guidance

Repository knowledge is intentionally split.

## AGENTS.md

General project guidance.

Contains:

* coding conventions
* architecture
* verification rules

## .agent/maps/automaton.md

Compact repository navigation.

Purpose:

* reduce repository exploration
* guide normal-mode orchestration

Micro tasks should normally avoid reading maps unless search fails.

## .agent/agents/

Role behaviour.

Examples:

```text
orchestrator.md
developer.md
reviewer.md
```

## .agent/skills/

Reusable implementation knowledge.

Skills describe *how* to perform recurring tasks. Micro tasks should normally avoid skills unless the task cannot be implemented safely without them.

---

# Recovery Strategy

## Developer Failure

If:

```text
code changed
AND
Codex failed
```

the harness generates:

```text
result.md
developer-error-summary.txt
```

allowing reviewer or human review to continue.

## Reviewer Failure

Reviewer failures generate:

```text
reviewer-error-summary.txt
```

without losing the current review state.

---

# Verification Strategy

Current behaviour:

Developer usually performs verification.

Typical commands:

```text
npm run type-check
npm run build
dotnet build
dotnet test
```

## Future Improvement

Move verification into the harness. The harness should execute verification and generate:

```text
verification-summary.md
verification-build.log
verification-test.log
```

Reviewer should read only the summary.

---

# Context Reduction Strategy

The primary optimisation goal is reducing context.

## Current

Reviewer reads:

```text
review-input.md
```

Developer reads:

```text
developer-input.md
```

Orchestrator navigates using:

```text
.agent/maps/automaton.md
```

instead of broad repository searches.

Micro and tiny modes skip orchestrator and reviewer, and `prepare-developer-input.ps1` omits large context by default.

Reviewer fix iterations reuse the original task/context and latest review instead of re-running the orchestrator.

## Future

Future improvements include:

* candidate-file pre-search artifacts
* symbol-level context
* line-range context
* cached repository maps
* automatic verification summaries

---

# Metrics Strategy

Each role records token usage.

```text
scope-classifier-metrics.json
orchestrator-metrics.json
developer-metrics.json
reviewer-metrics.json
```

For multi-iteration runs, the loop copies developer/reviewer metrics after each pass:

```text
developer-metrics.iteration-1.json
reviewer-metrics.iteration-1.json
developer-metrics.iteration-2.json
reviewer-metrics.iteration-2.json
```

The loop aggregates classifier, orchestrator, and all iteration metrics into:

```text
metrics.json
```

Metrics help identify expensive stages and validate optimisation work. Aggregated metrics include the scope classifier and every developer/reviewer iteration so the total run cost is not underreported.

---

# Current Optimisation Priorities

The current optimisation roadmap is:

1. Route one-file mechanical tasks through `micro`.
2. Keep `context.md` concise.
3. Cap `developer-input.md` by mode.
4. Cap `review-input.md` by mode.
5. Prefer search and exact candidate files over broad repository exploration.
6. Prefer `.agent/maps/automaton.md` over broad repository exploration for normal mode only.
7. Reduce orchestrator repository scanning.
8. Move verification into the harness.
9. Eventually provide symbol-level context instead of entire files.

---

# Entry Points

## New Task

```powershell
start-agent-task.ps1
```

Starts a fresh task from `goal.md`. Use this after creating or editing `goal.md`.

## Continue Existing Task

```powershell
run-agent-loop.ps1
```

Continues an existing generated task. Use this after interruptions or manual harness fixes, not after editing `goal.md`.

## Individual Roles

```powershell
run-orchestrator.ps1
run-developer.ps1
run-reviewer.ps1
```

Use individual role scripts only for debugging or manual override.

---

# Future Vision

The long-term goal is for the Agent Harness to function as an AI-native build pipeline.

Rather than simply executing prompts, it should:

* coordinate specialised AI roles
* minimise token usage through prepared context
* recover automatically from failures
* provide deterministic execution
* continuously improve through metrics and documentation updates

The harness should evolve in the same way that CI/CD systems evolved: from simple scripts into a structured, observable, and reliable orchestration framework for AI-assisted software development.
