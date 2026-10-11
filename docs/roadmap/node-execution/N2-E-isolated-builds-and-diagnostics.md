# N2-E — isolated builds and diagnostics
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N2-A, N2-B, N2-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Run build steps in separate bounded disposable OCI containers, with scoped build inputs and secrets, no management socket, restricted network, accounted image/cache/staging storage and verified artifact output. Reuse existing build pipeline where it satisfies this contract.

Expose applied policy, last verified usage, desired/observed revision and actionable failure reason through existing API/Admin paths. Full M3 charts are deferred; missing/stale measurements remain unknown.

Tests: malicious build command cannot request host privileges, deadline/cancel, disk pressure, secret redaction, cleanup after crash and aggregate limits. Exit: one complete build/deploy journey works with resource/privilege separation.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Reuse current build pipeline but execute untrusted build commands only in separate bounded workload; reject privileged/insecure options, host mounts and management-socket access.
2. Authorize/scoped materialization of source/secrets and disposable workspace; apply network/deadline/log/scratch/concurrency policy.
3. Verify and publish exact immutable output; account image/cache/staging disk and clean disposable artifacts after confirmed termination.
4. Expose minimal applied policy, desired/observed revision, measurement freshness and structured failure cause through existing typed API/Admin surfaces.

## Deliverables

Isolated build path and end-to-end build/deploy tests; results/N2-E-build-evidence.md distinguishes engine shared overhead from quota-covered bytes.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Build cannot access Node credentials/socket or another account's data | Build cannot access Node credentials/socket or another account's data. |
| Cancellation/deadline/crash terminates or records uncertain occupancy | no premature budget release. |
| Disk/log/output overflow fails clearly and healthy serving/management remain available | Disk/log/output overflow fails clearly and healthy serving/management remain available. |
| Stale/missing usage appears unknown | CLI success waits for deployment terminal semantics. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N3-A. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N2-E ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
