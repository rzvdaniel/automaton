# N1-D — installation update and doctor
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N1-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Build versioned installer/update/rollback/doctor for the initial Linux/x64 host contract; do not claim Windows/ARM support without proof. Keep package verification, host admission checks and Admin docs. Doctor explains failures without changing host storage drivers automatically.

Tests: fresh install, rerun, bad package/hash, partial install, safe update, service failure and credential revocation in a disposable host. Exit: operator can install/recover Node without undocumented steps; old workloads unchanged.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Implement versioned Linux/x64 install/update/service packaging using existing release machinery and verified package provenance.
2. Preflight supported host admission without automatic storage-driver/root-filesystem migration; make setup resumable after partial failure.
3. Define update/quiesce/journal compatibility and tested rollback; daemon restart must not terminate independent healthy workloads.
4. Provide read-only doctor with clear reason/freshness/eligibility; implement credential revocation and safe uninstall preserving volumes by default.

## Deliverables

Installer/update/doctor documentation and disposable installation fixtures; results/N1-D-installation-evidence.md lists tested OS/engine versions and unsupported modes.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Clean install and repeated setup produce one service/identity | Clean install and repeated setup produce one service/identity. |
| Bad digest/package fails before replacement | interruption resumes predictably. |
| Update preserves identity/journal and old apps stay running where contract permits | Update preserves identity/journal and old apps stay running where contract permits. |
| Uninstall cannot implicitly remove tenant volumes | doctor causes no mutation. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N2-A. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N1-D ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
