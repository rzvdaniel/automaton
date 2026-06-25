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

The harness is built around five principles.

## 1. Single Responsibility

Each agent has one clear responsibility.

* Scope Classifier chooses the execution strategy.
* Orchestrator plans the work.
* Developer modifies code.
* Reviewer validates the implementation.

No role should perform another role's responsibilities.

---

## 2. Recoverability

AI failures should never lose useful work.

If code changes already exist, the harness should generate recovery files instead of aborting.

---

## 3. Determinism

Every execution produces a reproducible trail.

Generated files describe exactly what happened and allow another agent—or a human—to continue from the current state.

---

## 4. Minimal Context

Every role should receive only the information it needs.

Large repository exploration should happen once.

Subsequent roles should reuse prepared context rather than rediscovering it.

---

## 5. Human Override

Every stage can be inspected, edited, or rerun manually.

The harness assists the user but never removes control.

---

# Overall Flow

```text
goal.md
    │
    ▼
Scope Classifier
    │
    ├── Tiny
    │       ▼
    │   Developer
    │
    ├── Small
    │       ▼
    │   Developer
    │       ▼
    │   Reviewer
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
     accept / fix
            │
      if fix
            │
            ▼
      Developer
            │
            ▼
        Reviewer
```

The loop continues until:

* accepted
* stopped
* maximum iterations reached

---

# Execution Modes

## Tiny

Input:

* `goal.md`

Runs:

* Developer

Used for trivial changes.

Manual review is expected.

---

## Small

Input:

* `goal.md`

Classifier generates:

* `task.md`

Runs:

* Developer
* Reviewer

No orchestrator is required.

---

## Normal

Runs:

1. Scope Classifier
2. Orchestrator
3. Developer
4. Reviewer

Used for larger or cross-project work.

---

# Responsibilities

## Scope Classifier

Purpose:

Determine:

* execution mode
* implementation complexity
* recommended maximum iterations

Produces:

```
scope.json
```

Small/Tiny modes also generate:

```
task.md
```

---

## Orchestrator

Purpose:

Convert a user goal into a precise implementation task.

Reads:

```
goal.md
review.md (optional)

.agent/maps/automaton.md
.agent/agents/orchestrator.md
```

Produces:

```
task.md
context.md
```

Does **not** modify application code.

Responsibilities:

* identify relevant files
* identify relevant components
* reduce repository exploration
* prepare developer context

---

## Developer

Purpose:

Implement code.

Reads:

```
developer-input.md
```

Generated from:

```
task.md
context.md
review.md
```

Produces:

```
result.md
```

Developer is the **only** role allowed to modify code.

If Codex crashes after modifying files, the harness generates:

```
result.md
developer-error-summary.txt
```

allowing reviewer to continue.

---

## Reviewer

Purpose:

Validate the implementation.

Reads:

```
review-input.md
```

Generated from:

```
task.md
context.md
result.md
git status
git diff --stat
capped git diff
developer-error-summary.txt
```

Produces:

```
review.md
```

Possible decisions:

```
accept
fix
stop
```

---

# Task Lifecycle

A new task always starts from:

```
AgentRuns/current/goal.md
```

The user edits **only** `goal.md`.

---

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

---

## Continue Existing Task

Execute:

```powershell
.\.agent\harness\run-agent-loop.ps1
```

This continues an already generated task.

It should **never** be used immediately after editing `goal.md`.

---

# Stale Task Protection

The harness prevents stale planning.

`run-agent-loop.ps1` checks:

```
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

```
goal.md
```

---

## Planning

```
scope.json

task.md

context.md
```

---

## Developer

```
developer-input.md

result.md

developer-error-summary.txt
```

---

## Reviewer

```
review-input.md

review.md

reviewer-error-summary.txt
```

---

## Metrics

```
classifier-metrics.json

orchestrator-metrics.json

developer-metrics.json

reviewer-metrics.json

metrics.json
```

---

## Raw Logs

```
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

---

## .agent/maps/automaton.md

Compact repository navigation.

Purpose:

Reduce repository exploration.

Contains:

* projects
* important folders
* important files
* verification commands

---

## .agent/agents/

Role behaviour.

Examples:

```
orchestrator.md
developer.md
reviewer.md
```

---

## .agent/skills/

Reusable implementation knowledge.

Skills describe *how* to perform recurring tasks.

---

# Recovery Strategy

## Developer Failure

If:

```
code changed

AND

Codex failed
```

the harness generates:

```
result.md

developer-error-summary.txt
```

allowing reviewer to continue.

---

## Reviewer Failure

Reviewer failures generate:

```
reviewer-error-summary.txt
```

without losing the current review state.

---

# Verification Strategy

Current behaviour:

Developer usually performs verification.

Typical commands:

```
npm run type-check

npm run build

dotnet build

dotnet test
```

## Future Improvement

Move verification into the harness.

The harness should execute verification and generate:

```
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

```
review-input.md
```

Developer reads:

```
developer-input.md
```

Orchestrator navigates using:

```
.agent/maps/automaton.md
```

instead of broad repository searches.

---

## Future

Future improvements include:

* symbol-level context
* line-range context
* cached repository maps
* automatic verification summaries

---

# Metrics Strategy

Each role records token usage.

```
classifier-metrics.json

orchestrator-metrics.json

developer-metrics.json

reviewer-metrics.json
```

The loop aggregates them into:

```
metrics.json
```

Metrics help identify expensive stages and validate optimisation work.

---

# Current Optimisation Priorities

The current optimisation roadmap is:

1. Keep `context.md` concise.
2. Cap `developer-input.md`.
3. Cap `review-input.md`.
4. Prefer `.agent/maps/automaton.md` over broad repository exploration.
5. Reduce orchestrator repository scanning.
6. Move verification into the harness.
7. Eventually provide symbol-level context instead of entire files.

---

# Entry Points

## New Task

```powershell
start-agent-task.ps1
```

---

## Continue Existing Task

```powershell
run-agent-loop.ps1
```

---

## Individual Roles

```powershell
run-orchestrator.ps1

run-developer.ps1

run-reviewer.ps1
```

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
