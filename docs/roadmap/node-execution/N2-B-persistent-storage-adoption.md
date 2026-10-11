# N2-B — persistent storage adoption
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N2-A, N0-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Reuse existing quota allocation and source write-lock mechanisms. Validate filesystem UUID/project ID/mount/ownership before attaching a volume. Preserve data paths in place where feasible; decouple mount ownership from a Hosted Runner lifetime. Use explicit volume references resolved by the Node, not caller-supplied host paths.

Tests: mismatched UUID/project, foreign mount, symlink/traversal, duplicate domain report, insufficient space, interrupted adoption and quota exhaustion. Compare application-consistent snapshots/checksums and verify actual quota enforcement. Exit: no volume recreation, quota bypass or data deletion to make adoption succeed.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Resolve authorized volume IDs locally to existing filesystem/project/mount records; reject caller paths and conflicting ownership.
2. Adopt same-host data in place with verified UUID/project/permissions and source/live separation; no volume recreation for convenience.
3. Reuse source filesystem locks and quota measurement identity; fence stale/wrong-domain reports and prevent duplicate accounting.
4. Make interrupted attach/adoption resumable without data loss; block unknown layout, unsafe shrink and insufficient headroom.

## Deliverables

Storage adoption logic plus isolated real-XFS fixtures; results/N2-B-storage-evidence.md preserves before/after identity and data verification.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Wrong UUID/project, foreign mount and symlink/traversal fail before attach | Wrong UUID/project, foreign mount and symlink/traversal fail before attach. |
| Same existing data and quota identity survive Node restart/adoption | Same existing data and quota identity survive Node restart/adoption. |
| Quota exhaustion prevents writes within documented domain | management remains usable. |
| Interrupted adoption can resume/roll back without deletion or hidden accounting reset | Interrupted adoption can resume/roll back without deletion or hidden accounting reset. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N2-C. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N2-B ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
