# N0-B — identity and state contracts
Status: specification-ready; [proposed contracts](results/N0-B-contracts.md), recorded 2026-10-12 against `64922354380276c5e0068515858769dbed61817f`. Independent security/tenancy/fencing review is pending. No implementation or live acceptance is claimed.
Mode: design. Prerequisites: N0-A.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Define Node versus existing RunnerHost mapping, enrollment/revocation, node incarnation and assignment generation. Preserve account/app IDs; classify Runner IDs as retained compatibility references or explicitly migrated execution targets. Define placement, operation/result DTOs and journal reuse. Select exactly one transport path by reusing RM; no parallel fallback.

Define the allowed host-operation set and how each gets validated. Reject tenant-controlled host paths, arbitrary privileged flags and execution of uploaded tenant code as a provisioning module. Decide current BYO tunnels/files API support explicitly.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Do not implement runtime changes in this slice. Produce concrete findings/contracts and test plans.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Choose Node identity mapping to existing RunnerHost records and list preserved versus migrated IDs, foreign keys and stored command references.
2. Specify enrollment, credential rotation/revocation, node incarnation and assignment generation separately. Define who increments each token, where it persists and where it is checked.
3. Specify request/command/result schemas against existing RM models: required/null fields, revision checks, bounds, serialization and error outcomes. Define transactional reservation/publication and duplicate handling.
4. Define a finite allowlist of typed host operations with authorization and privilege requirements. Inventory supported BYO tunnels/files and decide their target mapping.
5. Specify journal versioning, atomic writes, accepted/executing/terminal/uncertain outcomes and result acknowledgement retention. No claimed exactly-once external effects.

## Deliverables

Create results/N0-B-contracts.md with schema/DTO examples, key/index constraints, operation transition table, identity migration mapping, privilege table, transport reuse decision and unresolved decision-to-consumer links.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Two nodes cannot claim the same assignment by replaying credentials or results | Two nodes cannot claim the same assignment by replaying credentials or results. |
| A command with valid credentials but wrong account, incarnation, generation or digest is rejected before mutation | A command with valid credentials but wrong account, incarnation, generation or digest is rejected before mutation. |
| Lost result acknowledgement replays a recorded result | uncertain side effects reconcile or require explicit recovery. |
| No tenant API can submit raw Docker options or arbitrary host paths | No tenant API can submit raw Docker options or arbitrary host paths. |

For each scenario record automated/isolated-host/live evidence separately. Specify concrete tests and source evidence; do not run runtime mutation to manufacture acceptance.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N0-C. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N0-B ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified design work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
