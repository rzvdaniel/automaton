# Node execution-plane implementation briefs

The [master plan](../Automaton_Staged_Master_Plan.md) owns scope and sequencing. These 21 briefs elaborate every N0–N4 subsection; they do not introduce another roadmap.

**Next ready task: [N0-A](N0-A-responsibility-audit.md), source audit only.** Everything else is authored now and gated on its prerequisite findings. All runtime implementation, deployment and live-acceptance states remain pending.

Read [EXECUTION-CONTRACT.md](EXECUTION-CONTRACT.md) with each assigned brief. Each brief includes ordered work, required deliverables, failure scenarios, observations and a copyable task. Refine code-level contracts in the result record before implementing their consumers; do not wait for another author to provide a later brief.

## Slice index

| Slice | Brief | Prerequisites | Initial readiness |
| --- | --- | --- | --- |
| N0-A | [responsibility audit](N0-A-responsibility-audit.md) | none | audit-ready |
| N0-B | [identity and state contracts](N0-B-identity-and-state-contracts.md) | N0-A | dependency-gated |
| N0-C | [storage capacity and cutover design](N0-C-storage-capacity-and-cutover-design.md) | N0-B | dependency-gated |
| N0-D | [roadmap and acceptance contract](N0-D-roadmap-and-acceptance-contract.md) | N0-C | dependency-gated |
| N1-A | [node host and enrollment](N1-A-node-host-and-enrollment.md) | N0-D | dependency-gated |
| N1-B | [durable typed commands](N1-B-durable-typed-commands.md) | N1-A | dependency-gated |
| N1-C | [inventory and reconciliation](N1-C-inventory-and-reconciliation.md) | N1-B | dependency-gated |
| N1-D | [installation update and doctor](N1-D-installation-update-and-doctor.md) | N1-C | dependency-gated |
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

Store actual audit/specification/test/rollout evidence under results/ with the filename declared by the brief, and add its link here when created. No results files exist at authoring time. Keep current readiness separate from code-merged, tested, deployed and live-accepted states. Do not create placeholder results that imply work was done.

Later slices may perform preparation against code while live inventory is unavailable, but cannot pass the dependent cutover or release gate without evidence. Independent review for high-risk contracts is part of readiness; a model writing a brief is not that review.

## Execution scope

Default to one named slice and one cohesive change. The index does not grant permission for production operations. Follow current session authorization and repository procedures. The historical reference backlog remains outside the active implementation sequence.
