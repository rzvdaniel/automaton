# N4-A — migration tooling and restore
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N2-E, N3-D, N0-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Implement a bounded resumable migration operation per account/host: inventoried -> quiescing -> fenced -> adopted -> verified -> active; blocked/failed remain observable. Use generation/transaction guards and explicit storage mappings. Inventory backup coverage for database, key material, artifacts, volumes and required configuration. Rehearse an application-consistent restore in an isolated environment before moving retained state.

Tests: interruption at every boundary, duplicate migration, old delayed command, restored database with stale ownership, resource overlap and partially applied policy. Exit: migration is repeatable; rollback compatibility is documented and tested. Before changing data formats, identify whether binary rollback is valid or coordinated restore is required.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Implement resumable per-account migration using N0 phases, versioned identity mappings and durable generation/transaction guards.
2. Preflight backup coverage and application consistency for database, key material, artifacts and volumes; reject incomplete checkpoints.
3. Implement stop/revoke/resurrection prevention proof before adoption; preserve quota/data identities and overlap reservation accounting.
4. Rehearse interruption and restore in isolated fixture; define pre/post-write rollback bounds and operator recovery commands.

## Deliverables

Migration tooling plus results/N4-A-restore-and-migration.md with checkpoints, state transitions, compatibility limits and observed restore results.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Crash at every phase resumes without dual ownership or duplicated resources | Crash at every phase resumes without dual ownership or duplicated resources. |
| Restored old control state cannot reactivate stale executor credentials | Restored old control state cannot reactivate stale executor credentials. |
| Backup restores stateful marker and decryptable required secrets/configuration in isolated environment | Backup restores stateful marker and decryptable required secrets/configuration in isolated environment. |
| Irreversible conversion blocks untested binary rollback | no silent data deletion. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N4-B. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N4-A ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
