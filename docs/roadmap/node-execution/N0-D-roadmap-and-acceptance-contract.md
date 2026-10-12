# N0-D — roadmap and acceptance contract
Status: [consolidation specified](results/N0-D-architecture-and-acceptance.md); independent review and N0 exit pending. No implementation or live acceptance is claimed.
Mode: design. Prerequisites: N0-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Classify M2–M7 requirements as preserved, absorbed, deferred or unsupported; identify no more than three actual migration blockers. Produce ADR, contract examples, migration state machine, failure matrix and exact N1-A brief. Architecture tests must cover new dependencies and prevent tenant execution inside the privileged management service.

**Exit:** contracts reviewed, storage preservation specified, ownership fencing implementable, coherent support scope and no contradiction in canonical roadmap. Unknown live status remains unknown.

The N0-D result records current N1 preparation/consumer gates; no N1 implementation is authorized by this brief.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Do not implement runtime changes in this slice. Produce concrete findings/contracts and test plans.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Combine N0 findings into an architecture decision record; resolve identity, transport, storage, route-writer and privilege decisions needed by N1/N2.
2. Build traceability from every mandatory master-plan guarantee to implementation slice, test fixture, expected observation and required live proof.
3. Classify historical M2–M7 items as preserved, absorbed, deferred with revisit trigger, or unsupported with product impact. Do not restore the removed roadmap as competing authority.
4. Specify isolated MySQL, Linux/XFS/cgroup and managed/BYO fixtures; document configuration/cost and safe teardown without provisioning them.
5. Prepare N1-A concrete code-owner/file map and test cases from audited source; name blockers rather than declaring all later contracts settled.

## Deliverables

Create results/N0-D-architecture-and-acceptance.md; update index statuses only from evidence. Include no more than three immediate blockers and readiness decisions for dependent slices.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| All N1 entry decisions are resolved with source-grounded contracts | All N1 entry decisions are resolved with source-grounded contracts. |
| Every accepted security/data guarantee has an observable test and owner | Every accepted security/data guarantee has an observable test and owner. |
| Historical acceptance remains separately labeled and deferred work remains incomplete | Historical acceptance remains separately labeled and deferred work remains incomplete. |
| Independent review record covers privileged execution, storage and ownership design | Independent review record covers privileged execution, storage and ownership design. |

For each scenario record automated/isolated-host/live evidence separately. Specify concrete tests and source evidence; do not run runtime mutation to manufacture acceptance.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N1-A. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N0-D ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified design work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
