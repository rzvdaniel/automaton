# N2-C — web deployment and routing
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N2-A, N2-B, N1-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Reuse immutable artifact/digest and candidate-health-promotion flow. Node creates restricted workload directly, preserves prior healthy version, changes route through the declared writer, and reconciles partial promotion. Node restart must not stop healthy apps merely because management restarted.

Tests: duplicate/parallel deploy, failed candidate, crash around route switch, late stale result, stop/start, rollback, expired policy and safe delete. Exit: one stateful reference API deploys/updates/rolls back under Node ownership with preserved data.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Reuse immutable artifact/candidate/promotion services; validate digest, authorized mounts and applied policy before create.
2. Launch web OCI workload under account budget with declared readiness and bounded restart policy; healthy old version remains during failed candidate.
3. Apply routes through the one specified writer with durable revision/atomic publication; preserve public/private authentication semantics.
4. Reconcile failures around create/readiness/route switch/cleanup and serialized parallel deploy; distinguish unpublish, stop and delete.

## Deliverables

Direct Node deployment path, route/promotion failure tests and stateful reference app; results/N2-C-web-evidence.md.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Failed candidate preserves prior route/app/data | Failed candidate preserves prior route/app/data. |
| Crash before/after route promotion converges to declared revision | stale results cannot undo newer deploy. |
| Node/API restart does not terminate healthy independently serving apps | Node/API restart does not terminate healthy independently serving apps. |
| Stop/start and release revert preserve declared data | delete cannot target foreign resources. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N2-D. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N2-C ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
