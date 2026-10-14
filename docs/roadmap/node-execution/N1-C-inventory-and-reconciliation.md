# N1-C — inventory and reconciliation
Status: the bounded read-only inventory/ownership DTO and validator preparation is implemented, independently reviewed and QA-tested in [N1-C evidence](results/N1-C-reconciliation-evidence.md). Inventory access, classification, ownership persistence, reconciliation, adoption, cleanup and live acceptance remain gated; N1-C as a whole is not complete.
Mode: implementation. Prerequisites: N1-B.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Observe only Automaton-owned resources with verified identity labels plus durable ownership records. Compare desired and observed state; repair narrowly under the current assignment generation. Never delete an unknown container/volume just because its name resembles a managed resource. Record cleanup debt.

Tests: crash after resource create but before callback, missing journal entry, foreign labels/resources, offline control plane, late result, interrupted cleanup. Exit: restart discovers owned work and produces truthful status without touching foreign resources.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Implement read-only inventory of owned Docker/cgroup/storage resources; correlate labels with durable identity records, not names alone.
2. Classify desired/observed matches, missing owned resources, orphan candidates, foreign resources and uncertain ownership.
3. Reconcile only current-generation allowed changes, serialize local mutations and revalidate ownership immediately before effect.
4. Persist bounded cleanup debt and observable recovery errors; avoid destructive inference during control-plane disconnection.

## Deliverables

Inventory/reconciliation services with focused ownership/failure tests; results/N1-C-reconciliation-evidence.md includes unknown/foreign-resource behavior.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Foreign container/volume and forged/incomplete labels are untouched | Foreign container/volume and forged/incomplete labels are untouched. |
| Crash-created owned resource is discovered without duplicate allocation | Crash-created owned resource is discovered without duplicate allocation. |
| Disconnected Node continues safe serving and defers uncertain management | Disconnected Node continues safe serving and defers uncertain management. |
| Repeated cleanup attempts converge without deleting referenced data | Repeated cleanup attempts converge without deleting referenced data. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N1-D. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N1-C ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
