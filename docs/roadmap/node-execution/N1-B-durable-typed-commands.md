# N1-B — durable typed commands
Status: pure DTO/codec/hash/timestamp and fixture-boundary slice implemented, independently reviewed and acceptance-tested in [N1-B evidence](results/N1-B-command-evidence.md). The stateful persistence/API contract proposal was independently reviewed without blocking findings and merged in PR #387 at `775622c8`. N1-A's local identity/activation-client implementation merged in PR #379; positive-incarnation checkpoint follow-up merged in PR #386. No command consumer is implemented. N0-D's independent B1 approval and external-authority/deployed-path gates remain distinct. Dispatch, persistence, journal, broker and security-sensitive consumers remain blocked pending actual B1 approval; no live acceptance or full N1-B exit is claimed.
Mode: stateful contract review; prerequisites for consumer implementation: N1-A authority integration and approved B1 security/tenancy/fencing contract.

**Stateful consumer readiness:** the code-level persistence/API proposal in [N1-B evidence](results/N1-B-command-evidence.md) pins the `RunnerExecutionOperation` Node columns and nullability, MySQL indexes, claim/command transaction boundary, result-versus-expiry CAS/readback, and HTTP status/body/retry matrix. Its design review is complete for proposal scope; required MySQL execution and consumer acceptance belong to implementation. Actual B1 security/tenancy/fencing approval and N1-A operational authority gates remain distinct prerequisites.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Implement one harmless lab command first, using existing command/outcome infrastructure and node-scoped queue permissions. Validate operation target, generation, policy and artifact identity before any action. Journal intent and outcome, bound consumption, isolate management from workload saturation and retry outcome reporting after API outages.

Tests: duplicate command, lost acknowledgement, broker reconnect, restart before/after effect, stale command, revoked node, cross-node queue access. Exit: repeat delivery converges without duplicate resource creation; uncertain effect is reconciled rather than blindly rerun.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Implement a harmless inspect/report command first using the established RM transport and scoped node queue/API permissions.
2. Validate command identity and allowed operation before journaling execution; persist intent before host effects and outcome before acknowledgement/report retry.
3. Add bounded consumer management, reconnect and independent management/output capacity; avoid duplicate retry authorities.
4. Implement deterministic reconciliation of interrupted command and lost callback; retain uncertain state for effects that cannot safely be repeated.

## Deliverables

Typed command handler, journal/result retry tests and protocol docs; results/N1-B-command-evidence.md records crash boundaries and acknowledgement behavior.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Duplicate delivery creates one effect or reconciles one intended resource | completed outcome is replayed. |
| Crash after effect before outcome does not blindly repeat destructive work | Crash after effect before outcome does not blindly repeat destructive work. |
| API unavailable does not stop existing workloads | queued reports resume after recovery. |
| Stale assignment and cross-node consumption are denied before host mutation | Stale assignment and cross-node consumption are denied before host mutation. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

Complete independent review of the stateful persistence/API proposal. Do not implement dispatch, persistence, journal or broker consumers until the contract is accepted and the applicable B1 security/tenancy/fencing approval plus N1-A authority prerequisites are actually available. N1-C host inventory/reconciliation remains blocked on those N1-B/N1-A consumer gates; its pure DTO preparation is already complete.

## Copyable task

> Execute N1-B ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
