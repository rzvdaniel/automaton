# Node execution-plane implementation briefs

The [master plan](../Automaton_Staged_Master_Plan.md) owns scope and sequencing. These 21 briefs elaborate every N0–N4 subsection; they do not introduce another roadmap.

**Current task: close the N1-A positive-incarnation checkpoint follow-up** at `325c97b1`; independent review and isolated Linux acceptance are complete, and the follow-up is awaiting PR review. PR #379 merged the bounded local identity/activation-client implementation at `f524c2bd`, based on the approved specification `73b4b6e5`; see [N1-A evidence](results/N1-A-local-identity-activation-client.md). PR #377's local startup/installation-lock slice is merged; see [its acceptance record](results/N1-A-node-local-startup-lock.md). The activation persistence/CAS/retry slice merged in PR #376; see [activation persistence evidence](results/N1-A-activation-persistence.md). PR #370 merged the limited non-enrolled N1-A preparation; see [preparation evidence](results/N1-A-node-enrollment.md). PR #384 merged the pure N1-B command codec, N1-C read-only inventory models and N1-D doctor formatter at `9f8b4c4a`. The Node host does not compose the activation client and remains `RecoveryRequired`. Operational activation remains disabled pending HFA trust, a production recovery-epoch authority, independent security/operations approval and deployed-path evidence; mTLS selection remains a proposed security decision. No broker consumption, Docker access, workload execution or host effect is enabled.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) with each assigned brief. Each brief includes ordered work, required deliverables, failure scenarios, observations and a copyable task. Refine code-level contracts in the result record before implementing their consumers; do not wait for another author to provide a later brief.

## Slice index

| Slice | Brief | Prerequisites | Current readiness |
| --- | --- | --- | --- |
| N0-A | [responsibility audit](N0-A-responsibility-audit.md) | none | source audit complete; [evidence](results/N0-A-responsibility-audit.md) |
| N0-B | [identity and state contracts](N0-B-identity-and-state-contracts.md) | N0-A | specification-ready amendment; [proposal](results/N0-B-contracts.md), review pending |
| N0-C | [storage capacity and cutover design](N0-C-storage-capacity-and-cutover-design.md) | N0-B | design specified; [proposal](results/N0-C-migration-design.md), review/live evidence pending |
| N0-D | [roadmap and acceptance contract](N0-D-roadmap-and-acceptance-contract.md) | N0-C | [consolidation specified](results/N0-D-architecture-and-acceptance.md); review/exit pending |
| N1-A | [node host and enrollment](N1-A-node-host-and-enrollment.md) | N0-D | [limited preparation](results/N1-A-node-enrollment.md), [activation persistence/CAS/retry](results/N1-A-activation-persistence.md), [local startup/installation lock](results/N1-A-node-local-startup-lock.md), and [local identity/activation client](results/N1-A-local-identity-activation-client.md) merged; positive-incarnation checkpoint follow-up `325c97b1` awaiting PR review; operational activation disabled |
| N1-B | [durable typed commands](N1-B-durable-typed-commands.md) | N1-A | pure typed wire contracts and codec implemented and tested; stateful command admission, journal, broker and result consumers blocked pending N1-A operational authority and reviewed persistence/dispatch contracts; [evidence](results/N1-B-command-evidence.md) |
| N1-C | [inventory and reconciliation](N1-C-inventory-and-reconciliation.md) | N1-B | pure unverified inventory model/validator implemented and tested; discovery, ownership verification and reconciliation blocked pending owner schema and N1-A/B consumers; [evidence](results/N1-C-reconciliation-evidence.md) |
| N1-D | [installation update and doctor](N1-D-installation-update-and-doctor.md) | N1-C | pure diagnostic formatter implemented and tested; installer/update/host doctor blocked pending N1-A–C, package-provenance decisions and disposable host fixtures; [evidence](results/N1-D-installation-evidence.md) |
| N2-A | [account resources and admission](N2-A-account-resources-and-admission.md) | N1-D, N0-C | dependency-gated |
| N2-B | [persistent storage adoption](N2-B-persistent-storage-adoption.md) | N2-A, N0-C | dependency-gated |
| N2-C | [web deployment and routing](N2-C-web-deployment-and-routing.md) | N2-A, N2-B, N1-C | dependency-gated |
| N2-D | [shared static hosting](N2-D-shared-static-hosting.md) | N2-A, N1-C; N2-C route contract | dependency-gated |
| N2-E | [isolated builds and diagnostics](N2-E-isolated-builds-and-diagnostics.md) | N2-A, N2-B, N2-C | dependency-gated |
| N3-A | [action packaging and compatibility](N3-A-action-packaging-and-compatibility.md) | N0-D, N2-E | dependency-gated |
| N3-B | [ephemeral action executor](N3-B-ephemeral-action-executor.md) | N3-A, N2-A, N2-B | dependency-gated |
| N3-C | [schedules cancellation and recovery](N3-C-schedules-cancellation-and-recovery.md) | N3-B | dependency-gated |
| N3-D | [action product and byo parity](N3-D-action-product-and-byo-parity.md) | N3-C, N2-C | dependency-gated |
| N4-A | [migration tooling and restore](N4-A-migration-tooling-and-restore.md) | N2-E, N3-D, N0-C | dependency-gated |
| N4-B | [disposable canary](N4-B-disposable-canary.md) | N4-A | dependency-gated |
| N4-C | [retained account cutover](N4-C-retained-account-cutover.md) | N4-B plus explicit production authorization | dependency-gated |
| N4-D | [legacy retirement](N4-D-legacy-retirement.md) | N4-C plus completed rollback retention conditions | dependency-gated |

## Stage gates

- N0: audited ownership, reviewed identity/state/privilege/storage/cutover contracts and acceptance traceability. No runtime implementation.
- N1: disposable Node installs, scoped durable commands and owned-resource reconciliation. No tenant migration.
- N2: direct web/static/build execution with aggregate admission, preserved quota domains and restricted build privilege.
- N3: finite manual/scheduled Actions with faithful packaging, cancellation and uncertain-effect handling.
- N4: tested migration/restore/canary, authorized retained-account cutover and proven retirement for declared scope.

## Results and review

Store actual audit/specification/test/rollout evidence under results/ with the filename declared by the brief, and add its link here when created. [N0-A responsibility audit](results/N0-A-responsibility-audit.md) records the 2026-10-12 source baseline, dispositions, recovery traces and unavailable live observations. Keep current readiness separate from code-merged, tested, deployed and live-accepted states. Do not create placeholder results that imply work was done.

[N0-B identity/state contracts](results/N0-B-contracts.md) record proposed Node mapping, locally checked fences, RM reuse, authorization/journal schemas and BYO/legacy support scope. The user's N0-A source review permits this specification work; it does not approve these eventual security contracts. PR #369 carries N0 documentation. [N0-C migration design](results/N0-C-migration-design.md) is now specified under subsequent user authorization; [N0-D consolidation](results/N0-D-architecture-and-acceptance.md) now records proposed final contracts and readiness; its reviewed-contract exit remains unsatisfied.

Later slices may perform preparation against code while live inventory is unavailable, but cannot pass the dependent cutover or release gate without evidence. Independent review for high-risk contracts is part of readiness; a model writing a brief is not that review.

## Execution scope

Default to one named slice and one cohesive change. The index does not grant permission for production operations. Follow current session authorization and repository procedures. The historical reference backlog remains outside the active implementation sequence.
