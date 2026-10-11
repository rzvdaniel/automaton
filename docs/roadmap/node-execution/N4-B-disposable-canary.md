# N4-B — disposable canary
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: acceptance. Prerequisites: N4-A.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Start on a disposable account with static, stateful web app, build and scheduled/manual Action. Verify exact releases, volume identity/data, quota readback, routes, authentication, logs and reservations. Interrupt API/Worker/broker/Node in controlled fixtures; verify no unexplained data loss or repeated effects.

Stop on stale executor mutation, unauthorized access, quota escape, missing result, unrecoverable data or loss of management under saturation. Preserve diagnosis evidence. Exit: canary and rollback pass the finite acceptance matrix.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Prepare explicit disposable tenant/host fixture with static, stateful API, build, manual and scheduled Actions; list releases and baseline data/quota.
2. Run canary migration and verify generation, credential revocation, routes/auth, data identities, resource readback, results and cleanup debt.
3. Interrupt API/Worker/broker/Node at planned boundaries in isolated environment; test prior app availability and uncertain result behavior.
4. Rehearse rollback within supported checkpoint; record stop conditions and actual outcome, not a pass based on unit tests.

## Deliverables

Create results/N4-B-canary-evidence.md with scenario / release / observation / expected / result / cleanup and rollback report.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| No stale mutation, unauthorized access, quota escape, unexplained loss/repeated side effect or management starvation | No stale mutation, unauthorized access, quota escape, unexplained loss/repeated side effect or management starvation. |
| Stateful data and private-route protection survive migration/restart | Stateful data and private-route protection survive migration/restart. |
| Canary rollback restores one fenced executor owner and matching data/schema | Canary rollback restores one fenced executor owner and matching data/schema. |
| Failed mandatory scenario blocks retained-account cutover | Failed mandatory scenario blocks retained-account cutover. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N4-C. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N4-B ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified acceptance work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
