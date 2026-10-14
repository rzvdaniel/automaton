# N1-A — node host and enrollment
Status: [limited preparation](results/N1-A-node-enrollment.md), [activation persistence/CAS/retry](results/N1-A-activation-persistence.md), [Node local startup/installation-lock](results/N1-A-node-local-startup-lock.md), and the approved [durable local identity and activation-client implementation](results/N1-A-local-identity-activation-client.md) are merged. Commit `325c97b1` contains the positive-incarnation checkpoint synchronization follow-up and is awaiting PR review; its independent review and isolated Linux acceptance are recorded in the evidence. Full enrollment/activation remains blocked pending independent approval of selected S-B1 authentication and affected N0 contracts per [N0-D](results/N0-D-architecture-and-acceptance.md#n1-readiness-and-exact-first-implementation-scope), plus the prerequisites recorded in the specification. No operational Node or live acceptance is claimed.
Mode: implementation. Prerequisites: N0-D.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) and the [master plan](../Automaton_Staged_Master_Plan.md). Prerequisites must have actual outputs/evidence; a task title or a successful unrelated build is insufficient.

## Intended outcome and scope

Reuse/extract verified Runner lifecycle code, credential storage and RM client machinery. Choose existing project evolution or a small dedicated project based on N0, not a full rewrite. Add service startup/shutdown and read-only capability inventory: OS/architecture, engine/storage configuration, cgroup support, filesystem identities and eligible workload kinds. Health does not imply admission eligibility.

Before enrollment/activation implementation, review the [N1-A activation/recovery specification](results/N1-A-activation-recovery-spec.md) and the follow-on [durable local identity and activation-client specification](results/N1-A-local-identity-activation-client-spec.md). Their CAS, lock ownership, realm/epoch, atomic local persistence, retry/readback, receipt-validation and RecoveryRequired contracts refine this brief; they do not authorize operational activation or privileged effects by themselves. The merged local startup slice provides the host-scoped OS lock; the submitted implementation adds protected local identity/checkpoint validation and an uncomposed activation client plus read-only API readback. Startup remains RecoveryRequired, and no authenticated RecoveryOnly session, operational activation or privileged effects are enabled.

Tests: rejected/repeated enrollment, rotation/revocation, stale incarnation, invalid capability report, shutdown and secret redaction. Exit: disposable node registers, authenticates, reports capabilities and unregisters predictably.

## Before starting

1. Record git status, branch and HEAD; preserve existing changes.
2. Read prerequisite results linked from [README.md](README.md). Verify their decisions still match current source; explicitly identify missing results.
3. Map code/test/documentation owners with rg. Use current paths, not guessed names from historical plans.
4. Complete the code-level specification step in EXECUTION-CONTRACT.md before dependent implementation. Missing identity/schema/security/recovery decisions block their consumers, not unrelated tasks.
5. List one cohesive next change. If already implemented, collect missing evidence or report completion; do not duplicate it.

## Ordered work

1. Select existing Runner evolution or small Node project per N0 ADR; reuse lifecycle/auth/client code without copying a second complete management stack.
2. Implement node-scoped enrollment and atomic identity persistence, rotation/revocation and incarnation behavior; do not let repeated setup allocate duplicate identities.
3. Add bounded startup/shutdown and read-only capability reporting for OS/architecture, engine, storage identity/cgroup support; distinguish liveness from admission eligibility.
4. Wire typed models through existing API/DI/config patterns and update architecture dependency coverage; keep tenant workloads off the new service.

## Deliverables

Implement minimal Node service plus enrollment/API tests and diagnostic documentation; record actual project/symbol ownership in results/N1-A-node-enrollment.md.

Also update the index with actual readiness and link the result. For changed runtime behavior, update the relevant current architecture/Admin documentation under repository rules. Do not mark later stages complete.

## Mandatory scenarios and expected observations

| Scenario / invariant | Required observation |
| --- | --- |
| Repeated enrollment converges to one intended identity | foreign/revoked credential fails. |
| Restart preserves journal/identity as specified | stale incarnation results cannot change assignments. |
| Unsupported engine/storage reports ineligible, not healthy/zero capacity | Unsupported engine/storage reports ineligible, not healthy/zero capacity. |
| Shutdown observes cancellation and no credentials appear in logs | Shutdown observes cancellation and no credentials appear in logs. |

For each scenario record automated/isolated-host/live evidence separately. Implement deterministic regressions for changed behavior and run applicable real fixtures for kernel/transaction guarantees. Never report a fake-based test as proof of host enforcement.

## Failure and recovery review

Trace duplicate input, crash before/after durable state or host effect, stale generation/policy, revoked/foreign identity, cancellation, unavailable control plane and partial cleanup. Record which scenarios apply and why. Persistent data is retained unless an explicitly authorized operation defines its deletion. An ambiguous result is observable uncertainty, not success or permission to retry external effects.

## Completion gate

The deliverables and mandatory scenarios are satisfied for the declared scope, prerequisite contracts have not been contradicted, and all unverified/live-only work is explicit. A docs/design result does not complete runtime migration. No production operation is authorized by this brief.

## Next task

N1-B. Proceed only when its prerequisites are satisfied. If this slice needs smaller PRs, retain this gate and list ordered subtasks in its results file.

## Copyable task

> Execute N1-A ONLY. Read this brief, EXECUTION-CONTRACT.md, AGENTS.md and relevant skills. Verify prerequisite evidence and current code, then perform the specified implementation work. Resolve routine choices within the master plan, record contracts before coding, and do not silently guess unresolved tenancy/security/migration decisions. Return the required handoff with exact evidence.
