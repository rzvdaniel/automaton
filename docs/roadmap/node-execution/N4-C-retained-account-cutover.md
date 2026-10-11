# N4-C — retained account cutover
Status: authored; dependency-gated. No implementation or live acceptance is claimed.
Mode: operations. Prerequisites: N4-B plus explicit production authorization.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Use authorized migration windows, backup checkpoints and one account/host cohort at a time. Stop old executor and prevent resurrection before transferring ownership. Verify supported tunnels/files/workflows and stored execution references. Resume admission only after readback and application-level probes. Record previous/new releases, generations, mounted data identities and cleanup debt.

Rollback restores a single executor owner with a new fenced generation; never run old and new managers concurrently over the same resources. Exit: managed cohort and one representative BYO installation accepted, with limitations documented.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Inventory authorized cohort and maintenance window, exact current/new releases, recent verified backups and supported workload/BYO coverage.
2. Pause account mutations, resolve in-flight work and prove old executor stopped/revoked before transferring generation/ownership.
3. Adopt/reconcile and verify application-level probes, quota identity/readback, routes/auth, tunnels/files/workflows and outcome references.
4. Resume admission only after mandatory acceptance; document rollback boundary, follow-up monitoring and retained recovery resources.

## Deliverables

Create results/N4-C-cutover-evidence.md from real authorized observations, with redacted cohort/checkpoint/release mappings and cleanup debt.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Every retained account has verified data/route/policy and one live executor owner | Every retained account has verified data/route/policy and one live executor owner. |
| Old credential/queue/auto-restart cannot resurrect management after cutover | Old credential/queue/auto-restart cannot resurrect management after cutover. |
| Managed and representative BYO scope accepted or explicit blocker remains | Managed and representative BYO scope accepted or explicit blocker remains. |
| Before reopening writes rollback tested | after new writes use declared forward-repair/restore strategy. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. Production steps require explicit authorization plus repository deployment procedure and the specified preflight/rollback gates.

## Next task

N4-D. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N4-C ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified operations work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
