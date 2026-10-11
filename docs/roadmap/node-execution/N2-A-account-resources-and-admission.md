# N2-A — account resources and admission
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N1-D, N0-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Reuse existing policy/reservation authority; add a Node execution target rather than a second quota ledger. Establish account cgroup hierarchy with workload children, apply aggregate CPU/memory/PID policy and verify effective membership on create/restart/adoption. Node management remains outside account budgets.

Account for running/stopped resources according to actual reservation rules, pending candidates, builds, failed retained workloads and overlap with old Runner allocations. Never silently reduce account entitlement to fund the migration. Reject unknown/stale admission inputs; concurrency uses real transactions.

Tests: competing placements from independent database contexts, restart membership, failed limits, account saturation, OOM/PID pressure and responsive management in an isolated host. Exit: no admission bypass or unbounded temporary workload budget.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Extend existing transactional reservations to Node placement; retain one account entitlement authority and explicit locality.
2. Create account aggregate cgroup parent with workload children and effective CPU/memory/PID readback; keep Node management outside tenant budget.
3. Account for active/pending/failed-retained allocations, build/candidate overlap and transition debt; reject stale/unverified admission data.
4. Prove create/resize/restart/recovery concurrency using independent real MySQL transactions and isolated host enforcement.

## Deliverables

Reservation/cgroup changes and integration/kernel fixtures; results/N2-A-resource-evidence.md includes promised versus measured limits.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Two concurrent placements cannot oversubscribe the defined host/account budget | Two concurrent placements cannot oversubscribe the defined host/account budget. |
| Restart/adoption keeps every workload in correct aggregate cgroup | Restart/adoption keeps every workload in correct aggregate cgroup. |
| OOM/PID/CPU saturation is bounded and management remains responsive in isolated fixture | OOM/PID/CPU saturation is bounded and management remains responsive in isolated fixture. |
| Cancellation releases once only after occupancy is known | uncertain work retains visible reservation. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N2-B. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N2-A ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
