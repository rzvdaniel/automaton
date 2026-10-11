# N4-D — legacy retirement
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation-and-operations. Prerequisites: N4-C plus completed rollback retention conditions.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Remove per-account Hosted Runner provisioning, image/service dependencies, obsolete credentials/queues, duplicate dispatch paths and dead UI/settings after an agreed rollback window. Separately consolidate/remove the old Infrastructure Runner service when all host-administration responsibilities have migrated. Stop/remove containers without deleting their persistent volumes as a side effect.

Retain necessary historical identities through explicit migration/read models; no permanent dual executor fallback. If an unmigrated supported BYO cohort remains, isolate and name its temporary compatibility scope, tests, owner and exit condition. Do not claim platform-wide retirement while it remains.

Tests: new accounts create no Hosted Runner; migrated app/Action never targets retired executor; revoked identities cannot return; uninstall preserves data by default; no foreign-resource deletion. Update Admin, install, architecture, CLI and product documentation and dependency tests. Exit: no redundant installed management service or obsolete live execution path in the accepted scope.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Search all code/config/CI/installers/tests/docs for old Hosted/Infrastructure Runner consumers; classify historical records separately from live executor dependencies.
2. Remove retired provisioning, image/service/queue/credential/dispatch/UI paths only after each responsibility has accepted Node replacement.
3. Preserve retained volumes and historical identity/read mappings; keep any explicitly unmigrated BYO scope named with owner/removal gate rather than a hidden permanent fallback.
4. Update architecture/Admin/install/product docs and dependency tests; execute infrastructure cleanup only when explicitly authorized and ownership/retention conditions pass.

## Deliverables

Retirement changes and results/N4-D-retirement-evidence.md with dependency inventory, removed resources, retained data and scope limitations.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| New accounts allocate no permanent Hosted Runner | accepted workloads never dispatch to retired executor. |
| Revoked services cannot re-enroll/resurrect | uninstall keeps data unless separately authorized removal. |
| Dependency search has no unexplained live legacy consumer | foreign resources remain untouched. |
| Platform-wide completion is not claimed while an unsupported/unmigrated cohort remains | Platform-wide completion is not claimed while an unsupported/unmigrated cohort remains. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

next: P1 in master plan. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N4-D ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation-and-operations work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
