# N3-B — ephemeral action executor
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N3-A, N2-A, N2-B.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Use current execution IDs, journals/results and concurrency admission. Package exact action/module version; materialize authorized inputs; mount only declared account/app data; allocate bounded scratch; execute in separate container; collect bounded logs/result; clean disposable resources. Tenant code never runs in Node process.

Tests: success/failure, missing/hash-mismatched artifact, unauthorized input/volume, non-root/capability policy, deadline, OOM/PID, log overflow, secret leakage, cross-tenant network denial. Exit: manual Action has observable terminal state and no leftover permanent executor.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Materialize exact authorized Action inputs/artifact/secrets in bounded disposable workspace and resolve declared volumes.
2. Execute in nonprivileged ephemeral OCI container using Node command/journal infrastructure and existing execution identity.
3. Collect bounded/redacted logs and typed result; report completion durably before acknowledgement and clean only disposable owned resources.
4. Apply deadlines/OOM/PID/resource/network policy and record uncertainty when termination/result cannot be verified.

## Deliverables

OCI Action executor and success/failure/security fixtures; results/N3-B-executor-evidence.md.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Success/failure return correct execution identity/result with no permanent executor left | Success/failure return correct execution identity/result with no permanent executor left. |
| Wrong digest/foreign volume or credential fails before launch | Wrong digest/foreign volume or credential fails before launch. |
| OOM/deadline/log overflow produce explicit bounded terminal/uncertain state | OOM/deadline/log overflow produce explicit bounded terminal/uncertain state. |
| Tenant code never executes inside Node process or obtains Docker/control-plane access | Tenant code never executes inside Node process or obtains Docker/control-plane access. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N3-C. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N3-B ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
