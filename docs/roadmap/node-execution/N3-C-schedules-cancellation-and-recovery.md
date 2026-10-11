# N3-C — schedules cancellation and recovery
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N3-B.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Retain existing scheduler/dispatch identity. Reserve before execution; handle Worker crash around dispatch, duplicate due event, cancellation before start and while running, Node restart and lost callback. Persist cancellation tombstones/reconciliation so delayed delivery cannot resurrect canceled work. Release reservations only when execution termination is confirmed or reconciliation makes retained occupancy explicit.

Tests: allowance-one competitors never overlap, distinct execution IDs, pending cancellation after restart, uncertain external side effect not automatically repeated, management/log access during execution saturation. Exit: manual and scheduled Actions converge honestly without duplicate unintended execution.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Retain existing scheduler/dispatch ownership and transactional admission; map target to Node without a second job engine.
2. Implement/reuse durable cancellation tombstones and ordered checks before launch and during termination; delayed delivery cannot resurrect canceled work.
3. Reconcile worker/node crashes around reservation, dispatch, launch and reporting; preserve allowance-one fairness and unique execution IDs.
4. Release budgets only after verified termination; expose unknown effects and define manual/retry recovery policy.

## Deliverables

Schedule/cancel/recovery regressions and isolated execution fixtures; results/N3-C-scheduling-evidence.md.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Distinct executions with allowance one never overlap, including worker restart after reservation | Distinct executions with allowance one never overlap, including worker restart after reservation. |
| Cancellation before launch prevents delayed/replayed launch | Cancellation before launch prevents delayed/replayed launch. |
| Lost callback replays result | unknown external side effect is not automatically repeated. |
| Logs/results/management remain accessible while execution workers are saturated | Logs/results/management remain accessible while execution workers are saturated. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N3-D. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N3-C ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
