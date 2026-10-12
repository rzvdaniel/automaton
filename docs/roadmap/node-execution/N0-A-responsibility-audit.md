# N0-A — responsibility audit
Status: source audit complete; [result and limitations](results/N0-A-responsibility-audit.md), recorded 2026-10-12 at `1cc52af1266a482c3adff77d1928fa94c19d4bb0`. No implementation or live acceptance is claimed.
Mode: audit. Prerequisites: none.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Read AGENTS.md, relevant distributed/generic-runtime/deployment/documentation skills, canonical roadmap, RM contracts, current Admin guides and source/tests. Record branch/HEAD/status. Inventory Infrastructure Runner, Hosted Runner, app containers, builds, manual/scheduled Actions, file operations, tunnels, heartbeat, trusted infrastructure Actions, storage measurement, provisioning, policy transitions and cleanup.

Output: Responsibility | current owner/path | durable state | privilege | target owner | reuse/remove | migration evidence. Paths from older briefs are candidates to verify, not guaranteed current paths. Separate tenant Actions from allowlisted host-management operations.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Do not implement runtime changes in this slice. Produce concrete findings/contracts and test plans.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Trace registration and dependency wiring from API/Worker/Runner entry points to each actual consumer; use rg and follow references, not filenames alone.
2. Trace a complete provision, deploy, build, manual Action, scheduled Action, file read/write, tunnel and deletion path. For each identify caller authorization, durable transaction, transport, local side effect and result reporting.
3. Inventory every credential/socket/host mount and each persisted account/app/Runner/host/storage identity. Include background recovery and lifecycle cleanup.
4. Classify each responsibility as reuse, extract, replace, remove or unresolved; compare existing tests and historical acceptance with current HEAD.

## Deliverables

Create results/N0-A-responsibility-audit.md with Responsibility / path+symbol / input / durable authority / privilege / current executor / target owner / tests / release evidence / disposition. Include exact HEAD and unavailable observations.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Trace one successful and one interrupted operation end to end | each state transition has a concrete owner and source pointer. |
| Every discovered live consumer is assigned a disposition | unsupported/unavailable evidence is explicit. |
| No runtime code, infrastructure mutation or invented live acceptance | No runtime code, infrastructure mutation or invented live acceptance. |

For each scenario record automated/isolated-host/live evidence separately. Specify concrete tests and source evidence; do not run runtime mutation to manufacture acceptance.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N0-B. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N0-A ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified audit work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
