# N0-C — storage capacity and cutover design
Status: design specified in [N0-C migration design](results/N0-C-migration-design.md) under explicit user authorization using amended N0-B. Independent review and implementation/live evidence remain pending. No implementation or live acceptance is claimed.
Mode: design. Prerequisites: N0-B.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Inventory each current mounted path, quota domain, filesystem UUID, project ID, UID/GID, source/live/data layout and storage authority. Choose in-place same-host adoption first. Define account cgroup ownership without a Hosted Runner process, overlap accounting during transition, host reserve and candidate/build budgets.

Write the single-owner cutover protocol: pause account mutations -> resolve in-flight work -> fence old executor -> record Node ownership/generation -> reconcile/adopt resources -> verify policy and route -> resume. A stale command must be rejected locally before mutation. A database flag alone cannot stop a partitioned old executor; initial migration must stop/revoke it and prove it cannot resume. If that cannot be proven, do not cut over.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Do not implement runtime changes in this slice. Produce concrete findings/contracts and test plans.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Map every volume/source/live/data path to account/app, host filesystem UUID, XFS project ID, UID/GID, mount mode and writer; use redacted examples rather than production secrets.
2. Specify same-host adoption and policy for incompatible layout; block rather than silently move data. Preserve existing quota accounting and source-write synchronization.
3. Specify account cgroup ownership without permanent Runner, reserve hierarchy and temporary overlap accounting; define release of reservations only after confirmed termination/reconciliation.
4. Specify cutover phases with persistent transitions and preconditions, old executor stop/revoke proof, local stale-command rejection and post-adoption probes.
5. Design rollback checkpoints and database/data compatibility: binary rollback versus restore/forward repair; name every irreversible boundary.

## Deliverables

Create results/N0-C-migration-design.md with volume map, resource ledger, cutover/rollback transition tables and one fault scenario per boundary.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| A partitioned old executor cannot mutate after ownership transfer | if proof is absent migration remains blocked. |
| Same quota domain is not counted twice and account budget is not bypassed by candidates/builds | Same quota domain is not counted twice and account budget is not bypassed by candidates/builds. |
| Interruption before/after ownership change leaves an observable recoverable owner/state | Interruption before/after ownership change leaves an observable recoverable owner/state. |
| Rollback never activates two managers or removes retained data | Rollback never activates two managers or removes retained data. |

For each scenario record automated/isolated-host/live evidence separately. Specify concrete tests and source evidence; do not run runtime mutation to manufacture acceptance.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N0-D. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N0-C ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified design work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
