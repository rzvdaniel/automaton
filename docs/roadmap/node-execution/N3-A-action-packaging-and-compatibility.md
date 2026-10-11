# N3-A — action packaging and compatibility
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: design. Prerequisites: N0-D, N2-E.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Map every supported Action type, module dependency, working directory, artifact/secret access, persistent output, schedule and workflow continuation. Do not assume all Actions are finite or pure. Decide one containerization contract and explicit unsupported cases. Distinguish host administration from tenant Actions.

Exit: compatibility matrix and exact packaging changes; no silent semantic change to existing Actions.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Do not implement runtime changes in this slice. Produce concrete findings/contracts and test plans.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Trace manual/scheduled/workflow Actions, dependencies, invocation arguments, working directories, secrets and persistent outputs from current source/tests.
2. Specify one OCI packaging/entry/result/log contract with exact artifact version and explicit mount/network/resource declarations.
3. Classify nonfinite/native/platform-dependent or host-management Actions as supported, requiring conversion, or unsupported with deliberate error.
4. Define stable execution identity, schedule/continuation mapping and retry policy for uncertain effects; preserve source/history without privileged fallback.

## Deliverables

Create results/N3-A-action-contract.md with compatibility matrix, payload examples, packaging schema, continuation mapping and N3-B file/test plan.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Each supported Action has deterministic packaging and declared data/secret access | Each supported Action has deterministic packaging and declared data/secret access. |
| Tenant Action cannot become host-management script | Tenant Action cannot become host-management script. |
| Unsupported dependency receives visible error before dispatch | Unsupported dependency receives visible error before dispatch. |
| Retry of uncertain external effect is explicit and does not assume purity | Retry of uncertain external effect is explicit and does not assume purity. |

For each scenario record automated/isolated-host/live evidence separately. Specify concrete tests and source evidence; do not run runtime mutation to manufacture acceptance.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N3-B. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N3-A ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified design work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
