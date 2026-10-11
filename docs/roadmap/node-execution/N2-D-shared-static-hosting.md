# N2-D — shared static hosting
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N2-A, N1-C; N2-C route contract.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Adopt existing shared static path when available. Keep artifacts immutable, atomic publication, TLS/auth/organization checks and aggregate storage/count admission. Shared serving must expose only published assets, not source, secrets, logs or app volumes. Confirm routing/storage availability across supported control-plane replicas.

Tests: traversal/symlink archive, cross-account access, concurrent promotion, rollback and cleanup. Measure shared serving at 1/10/100 idle sites where feasible. Exit: static-only accounts do not need an idle Hosted Runner or per-site backend process.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Audit/reuse shared static serving first; do not add a new server when existing path meets contract.
2. Authorize immutable published asset promotion with account storage/count policy, bounded extraction and path protections.
3. Serve only explicit public artifact roots; preserve private-app auth/TLS and supported replica routing/storage consistency.
4. Implement atomic publish/revert/unpublish/delete and record shared-service versus account-storage costs.

## Deliverables

Shared static acceptance and security fixtures; results/N2-D-static-evidence.md with measured 1/10/100-site results as feasible.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Static account has no permanent account Runner or per-site process | Static account has no permanent account Runner or per-site process. |
| Archive traversal, symlinks and unintended source/ | Archive traversal, symlinks and unintended source/.env/volume paths cannot be served. |
| Concurrent publish/revert exposes one valid version and preserves prior version on failure | Concurrent publish/revert exposes one valid version and preserves prior version on failure. |
| Cross-account unauthorized private URL and management requests fail | Cross-account unauthorized private URL and management requests fail. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N2-E. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N2-D ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
