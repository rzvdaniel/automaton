# N3-D — action product and byo parity
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: implementation. Prerequisites: N3-C, N2-C.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Adapt CLI/MCP/Studio target selection internally while retaining user concepts of app/action/run/log/result. For migrated accounts, no user must provision a Hosted Runner solely to run an Action. Test workflows, file APIs, BYO routing/tunnels and disconnect behavior that N0 classifies as supported.

Exit: one schedule and manual success/failure/cancel journey run on managed and authorized BYO nodes. Unsupported behaviors have deliberate errors and documentation; they do not silently fall back to privileged execution.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Adapt existing CLI/MCP/Studio target logic to Node execution without exposing internal role topology to users.
2. Retain app/action/run/log/result and workflow continuation semantics under authorization; regenerate typed clients as needed.
3. Exercise supported BYO file/tunnel/private route and disconnected execution behavior defined by N0/N3-A.
4. Document unsupported conversions and actionable failure messages; remove requirement to create permanent Runner solely for Actions.

## Deliverables

Product adapters and managed/BYO journey evidence in results/N3-D-parity-evidence.md; Admin docs updated if behavior changes.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Same manual/scheduled success/failure/cancel flows use one execution contract on managed and BYO | Same manual/scheduled success/failure/cancel flows use one execution contract on managed and BYO. |
| Cross-account management/log/result/data access denied through every public surface | Cross-account management/log/result/data access denied through every public surface. |
| Disconnect is distinguished from runtime failure and completed results recover | Disconnect is distinguished from runtime failure and completed results recover. |
| No automatic fallback to privileged/legacy execution for unsupported Actions | No automatic fallback to privileged/legacy execution for unsupported Actions. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N4-A. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N3-D ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
