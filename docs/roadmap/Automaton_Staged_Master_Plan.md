# Automaton — Node-first staged master plan
Revised 2026-10-11. Proposed implementation roadmap; no runtime implementation or production change is claimed.

## Decision and shortest useful route

Start the Node execution-plane refactor now with N0. Do not finish the entire legacy M3–M6 program first. Carry their indispensable safety requirements into the Node slices that need them. Keep M0/M1 guarantees and reuse the completed RM transport work. Finish only M2 blockers required for a safe canary; prove the remaining compute scenarios against the target architecture instead of implementing them twice.

The mission remains: publish web apps and execute Actions economically on managed or customer-owned infrastructure. Begin with static serving, OCI web apps and ephemeral OCI Actions. Retain .NET, MySQL, CAP/RabbitMQ, Docker and Traefik. No Kubernetes, Cloudflare, S3/R2, new builder, new language or automatic volume migration is required.

One installed Node Agent per execution host replaces the management responsibilities of the Infrastructure Runner and permanent per-account Hosted Runners. It directly manages workloads. It never executes tenant scripts inside its own privileged process. The account remains the resource/authorization owner; an account container is no longer necessary to express that ownership.

Consolidation is an architectural simplification, not by itself a security improvement. The improvement must come from explicit trust boundaries, constrained workload execution and narrower management commands. Host Docker access is privileged and must stay inaccessible to tenants.

## Evidence and status

The previous packet recorded main at 57941d9971f5 on 2026-10-06: M1 accepted for its documented scope; RM accepted for the installed x64 cohort; M2 incomplete; PRs #366/#368 merged; #368 live acceptance and #367 status unresolved. These are historical observations from that packet, not a fresh repository audit. N0 must establish current code, deployment and acceptance truth. Do not repeat merged work or claim Node migration is already implemented.

The historical `docs/roadmap/managed-platform-completion-plan.md` was removed from current main at `9e18987a9bfcebd87909167d4eb4e5a6cb82cf6c` (2026-10-11). Use Git history for its acceptance evidence. This document is the proposed Node-first roadmap; N0 must reconcile current source and release evidence before recording implementation status. No milestone is complete merely because a brief exists.

## What to skip, absorb or defer

| Existing milestone / brief | Revised treatment | Minimum retained now | Deferred work |
| --- | --- | --- | --- |
| M0/M1 admission/storage | Preserve; regression-test affected paths | ownership, filesystem UUID, XFS quota identity, supported storage backend, source write locks | redesigning working storage |
| RM transport | Reuse, adapt target identity | durable delivery, scoped credentials, journal/results, cancellation | a second transport or another reconnect redesign |
| M2 / S01 | Split into migration gates | reservations, aggregate limits, bounded jobs, management headroom, safe deletion | proofs tied only to retired Runner topology |
| M3 / S02 | Defer full stage; absorb safety subset | applied policy, measurement identity/freshness, unknown states, clear admission failure | polished usage dashboards/history |
| M4 / S06 | Defer paid billing | existing authorization/entitlements; safe fixed policy and no unsafe shrink | checkout/webhook expansion, commercial plan transitions |
| M5 / S03 | Absorb core lifecycle into N2/N3/N4 | deploy/promote/rollback, Actions, cancellation, logs, safe deletion | broad UX, universal manifests, extra lifecycle features |
| M6 / S04 | Split by risk | rollback/restore proof, revocation, crash reconciliation, cleanup ownership | HA, all-platform matrices, full operational program |
| M7 / S05/S07 | Dogfood after N4; defer launch | small before/after cost baseline | paid pilot, final pricing, capacity expansion |
| S08/S09 | Optional isolated research | capability boundary and honest trust policy | production V8/microVM integration |

Deferred never means completed. Every deferred item needs a scope, reason and revisit trigger in the roadmap. Existing unsafe behavior must be fixed or disabled rather than justified by deferral.

## Order and release gates

| Stage | Outcome | Entry gate |
| --- | --- | --- |
| N0 | Responsibility audit, target contracts, cutover specification | start immediately; read-only |
| N1 | Node enrollment, durable commands, health and reconciliation | reviewed N0 contracts |
| N2 | Node-managed web apps, static publishing and builds | N1 plus relevant compute/storage safety |
| N3 | Node-managed ephemeral manual/scheduled Actions | N1/N2 resource and execution contracts |
| N4 | Canary, managed/BYO acceptance, retirement of old topology | N2/N3 plus tested migration/restore |
| P1 | Minimal resource UX, dogfood, cost report and portfolio | N4 stable |
| P2 | Operational hardening proportional to actual usage | observed P1 risks; required before broader managed promises |
| P3 | Billing and restricted paid pilot | optional; proven P1/P2 economics and recovery |
| R1/R2 | V8 / microVM experiments | isolated lab; justified workload or risk hypothesis |

N0 and N1 lab development need not wait for full M2 production acceptance. N2 live canary does require the relevant compute, storage, tenancy and rollback gates. N4 retirement requires every supported workload to be migrated or explicitly retained in a named compatibility scope with an owner and removal condition.

## Target ownership and execution architecture

| Responsibility | Authority / executor |
| --- | --- |
| account policy and authorization | existing control-plane models/API |
| placements and capacity reservations | existing durable control-plane owner, extended for Node targets |
| command publication | existing Worker/CAP path |
| host mutation and local reconciliation | one Node Agent, node-scoped credential and journal |
| app/build/Action execution | separate constrained OCI workloads |
| persistent storage | existing account/app identity and local XFS quota domains |
| public/private routing | current Traefik/static system; one declared route writer |
| operation results | durable control-plane state plus node retryable outcome journal |

Proposed control flow: CLI/Studio -> authorized API -> durable reservation/operation -> existing dispatch -> Node Agent -> restricted workload -> durable result/reconciliation -> API/Studio. API availability and a particular API instance must not own the lifetime of an app.

Use one management service initially, preferably a .NET host service under systemd on supported Linux, if N0 verifies deployment compatibility. A separate privileged helper is justified only if it meaningfully narrows privileges; do not add one as ceremony. Docker remains the initial OCI backend. Pin a verified host/engine configuration and preserve current storage admission constraints until a separately tested change replaces them.

Accounts may be pinned to one node initially. Reservations apply across apps/builds/Actions and temporary candidates. If multi-node accounts become supported, global entitlement accounting must remain authoritative; per-node limits alone do not enforce a global account budget. Data-local workloads cannot be moved to another host solely because that host has free compute.

### Contract sketch — resolve against existing models in N0

A workload command needs: operation ID, account/app identity, node ID, node incarnation, assignment generation, policy revision, desired workload revision, immutable artifact digest, authorized persistent-volume IDs, resource limits, deadline and idempotency/reconciliation metadata. Tenant inputs never supply arbitrary host mount paths, privileged flags or unrestricted Docker create JSON.

Use explicit workload kinds initially: static, web-OCI, build-OCI and Action-OCI. Auto-detection proposes build/runtime metadata; resolve it into a validated versioned deployment contract before scheduling. Do not infer that JavaScript implies V8 or that .NET requires a different platform path.

### Trust and resource rules

- Node credentials, broker access, Docker socket and host mounts never enter tenant environments.
- Enrollment has node-scoped identity, revocation and incarnation fencing; tenant API authorization is still mandatory.
- Run apps/builds/Actions with non-root identity where supported, bounded CPU/memory/PIDs/deadline/logs/scratch, dropped capabilities and reviewed seccomp/network policy. Builds are untrusted execution too.
- Keep management processes outside tenant cgroup budgets with explicit host headroom. Verify actual cgroup membership and aggregate enforcement on the supported host configuration.
- Account XFS projects, filesystem UUIDs and app volumes survive removal of a Runner. Never remap project IDs casually or count the same quota domain twice.
- Restrict network access to management endpoints and other tenants; intentional outbound internet is a separate policy. Prove the rules on the actual Docker/firewall configuration.
- Standard OCI containers share a host kernel. Until stronger isolation is reviewed, scope managed native-code execution to the accepted trust cohort. If hostile multitenancy is a launch requirement, microVM/hardened-runtime integration becomes a release gate for that cohort, not an optional security promise.
- Distributed commands cannot promise exactly-once external side effects. Persist intent/results, fence ownership and expose uncertain outcomes. Retry an uncertain Action only under its explicit idempotency/retry policy.

## N0 — Audit and approve the migration contract

**Goal:** one evidence-based architecture decision and a precise first implementation PR. No runtime code or infrastructure mutation.

### N0-A — Current responsibility map
Read AGENTS.md, relevant distributed/generic-runtime/deployment/documentation skills, canonical roadmap, RM contracts, current Admin guides and source/tests. Record branch/HEAD/status. Inventory Infrastructure Runner, Hosted Runner, app containers, builds, manual/scheduled Actions, file operations, tunnels, heartbeat, trusted infrastructure Actions, storage measurement, provisioning, policy transitions and cleanup.

Output: Responsibility | current owner/path | durable state | privilege | target owner | reuse/remove | migration evidence. Paths from older briefs are candidates to verify, not guaranteed current paths. Separate tenant Actions from allowlisted host-management operations.

### N0-B — Identity, privilege and state contracts
Define Node versus existing RunnerHost mapping, enrollment/revocation, node incarnation and assignment generation. Preserve account/app IDs; classify Runner IDs as retained compatibility references or explicitly migrated execution targets. Define placement, operation/result DTOs and journal reuse. Select exactly one transport path by reusing RM; no parallel fallback.

Define the allowed host-operation set and how each gets validated. Reject tenant-controlled host paths, arbitrary privileged flags and execution of uploaded tenant code as a provisioning module. Decide current BYO tunnels/files API support explicitly.

### N0-C — Migration, capacity and storage design
Inventory each current mounted path, quota domain, filesystem UUID, project ID, UID/GID, source/live/data layout and storage authority. Choose in-place same-host adoption first. Define account cgroup ownership without a Hosted Runner process, overlap accounting during transition, host reserve and candidate/build budgets.

Write the single-owner cutover protocol: pause account mutations -> resolve in-flight work -> fence old executor -> record Node ownership/generation -> reconcile/adopt resources -> verify policy and route -> resume. A stale command must be rejected locally before mutation. A database flag alone cannot stop a partitioned old executor; initial migration must stop/revoke it and prove it cannot resume. If that cannot be proven, do not cut over.

### N0-D — Roadmap reconciliation and test plan
Classify M2–M7 requirements as preserved, absorbed, deferred or unsupported; identify no more than three actual migration blockers. Produce ADR, contract examples, migration state machine, failure matrix and exact N1-A brief. Architecture tests must cover new dependencies and prevent tenant execution inside the privileged management service.

**Exit:** contracts reviewed, storage preservation specified, ownership fencing implementable, coherent support scope and no contradiction in canonical roadmap. Unknown live status remains unknown.

**First prompt:** Execute N0-A only in the existing checkout. Inspect and report current responsibilities, privileges, data identities and evidence. No replacement implementation, deployment or production mutations. Return the next smallest N0 slice.

## N1 — Minimal Node management plane

**Goal:** one node identity and reliable management lifecycle in a lab alongside current infrastructure; no tenant migration yet.

### N1-A — Host and enrollment
Reuse/extract verified Runner lifecycle code, credential storage and RM client machinery. Choose existing project evolution or a small dedicated project based on N0, not a full rewrite. Add service startup/shutdown and read-only capability inventory: OS/architecture, engine/storage configuration, cgroup support, filesystem identities and eligible workload kinds. Health does not imply admission eligibility.

Tests: rejected/repeated enrollment, rotation/revocation, stale incarnation, invalid capability report, shutdown and secret redaction. Exit: disposable node registers, authenticates, reports capabilities and unregisters predictably.

### N1-B — Durable typed host commands
Implement one harmless lab command first, using existing command/outcome infrastructure and node-scoped queue permissions. Validate operation target, generation, policy and artifact identity before any action. Journal intent and outcome, bound consumption, isolate management from workload saturation and retry outcome reporting after API outages.

Tests: duplicate command, lost acknowledgement, broker reconnect, restart before/after effect, stale command, revoked node, cross-node queue access. Exit: repeat delivery converges without duplicate resource creation; uncertain effect is reconciled rather than blindly rerun.

### N1-C — Local inventory and reconciliation
Observe only Automaton-owned resources with verified identity labels plus durable ownership records. Compare desired and observed state; repair narrowly under the current assignment generation. Never delete an unknown container/volume just because its name resembles a managed resource. Record cleanup debt.

Tests: crash after resource create but before callback, missing journal entry, foreign labels/resources, offline control plane, late result, interrupted cleanup. Exit: restart discovers owned work and produces truthful status without touching foreign resources.

### N1-D — Install and diagnostics
Build versioned installer/update/rollback/doctor for the initial Linux/x64 host contract; do not claim Windows/ARM support without proof. Keep package verification, host admission checks and Admin docs. Doctor explains failures without changing host storage drivers automatically.

Tests: fresh install, rerun, bad package/hash, partial install, safe update, service failure and credential revocation in a disposable host. Exit: operator can install/recover Node without undocumented steps; old workloads unchanged.

## N2 — Direct app, static and build execution

**Goal:** selected accounts can run apps without a permanent Hosted Runner.

### N2-A — Account resource ownership
Reuse existing policy/reservation authority; add a Node execution target rather than a second quota ledger. Establish account cgroup hierarchy with workload children, apply aggregate CPU/memory/PID policy and verify effective membership on create/restart/adoption. Node management remains outside account budgets.

Account for running/stopped resources according to actual reservation rules, pending candidates, builds, failed retained workloads and overlap with old Runner allocations. Never silently reduce account entitlement to fund the migration. Reject unknown/stale admission inputs; concurrency uses real transactions.

Tests: competing placements from independent database contexts, restart membership, failed limits, account saturation, OOM/PID pressure and responsive management in an isolated host. Exit: no admission bypass or unbounded temporary workload budget.

### N2-B — Persistent-storage adoption
Reuse existing quota allocation and source write-lock mechanisms. Validate filesystem UUID/project ID/mount/ownership before attaching a volume. Preserve data paths in place where feasible; decouple mount ownership from a Hosted Runner lifetime. Use explicit volume references resolved by the Node, not caller-supplied host paths.

Tests: mismatched UUID/project, foreign mount, symlink/traversal, duplicate domain report, insufficient space, interrupted adoption and quota exhaustion. Compare application-consistent snapshots/checksums and verify actual quota enforcement. Exit: no volume recreation, quota bypass or data deletion to make adoption succeed.

### N2-C — Web deployment and routing
Reuse immutable artifact/digest and candidate-health-promotion flow. Node creates restricted workload directly, preserves prior healthy version, changes route through the declared writer, and reconciles partial promotion. Node restart must not stop healthy apps merely because management restarted.

Tests: duplicate/parallel deploy, failed candidate, crash around route switch, late stale result, stop/start, rollback, expired policy and safe delete. Exit: one stateful reference API deploys/updates/rolls back under Node ownership with preserved data.

### N2-D — Static hosting without account daemon
Adopt existing shared static path when available. Keep artifacts immutable, atomic publication, TLS/auth/organization checks and aggregate storage/count admission. Shared serving must expose only published assets, not source, secrets, logs or app volumes. Confirm routing/storage availability across supported control-plane replicas.

Tests: traversal/symlink archive, cross-account access, concurrent promotion, rollback and cleanup. Measure shared serving at 1/10/100 idle sites where feasible. Exit: static-only accounts do not need an idle Hosted Runner or per-site backend process.

### N2-E — Untrusted builds and minimal diagnostics
Run build steps in separate bounded disposable OCI containers, with scoped build inputs and secrets, no management socket, restricted network, accounted image/cache/staging storage and verified artifact output. Reuse existing build pipeline where it satisfies this contract.

Expose applied policy, last verified usage, desired/observed revision and actionable failure reason through existing API/Admin paths. Full M3 charts are deferred; missing/stale measurements remain unknown.

Tests: malicious build command cannot request host privileges, deadline/cancel, disk pressure, secret redaction, cleanup after crash and aggregate limits. Exit: one complete build/deploy journey works with resource/privilege separation.

## N3 — Ephemeral Actions and schedules

**Goal:** finite work no longer requires a permanent account Runner.

### N3-A — Contract and compatibility inventory
Map every supported Action type, module dependency, working directory, artifact/secret access, persistent output, schedule and workflow continuation. Do not assume all Actions are finite or pure. Decide one containerization contract and explicit unsupported cases. Distinguish host administration from tenant Actions.

Exit: compatibility matrix and exact packaging changes; no silent semantic change to existing Actions.

### N3-B — Ephemeral OCI executor
Use current execution IDs, journals/results and concurrency admission. Package exact action/module version; materialize authorized inputs; mount only declared account/app data; allocate bounded scratch; execute in separate container; collect bounded logs/result; clean disposable resources. Tenant code never runs in Node process.

Tests: success/failure, missing/hash-mismatched artifact, unauthorized input/volume, non-root/capability policy, deadline, OOM/PID, log overflow, secret leakage, cross-tenant network denial. Exit: manual Action has observable terminal state and no leftover permanent executor.

### N3-C — Durable scheduling, cancellation and uncertain outcomes
Retain existing scheduler/dispatch identity. Reserve before execution; handle Worker crash around dispatch, duplicate due event, cancellation before start and while running, Node restart and lost callback. Persist cancellation tombstones/reconciliation so delayed delivery cannot resurrect canceled work. Release reservations only when execution termination is confirmed or reconciliation makes retained occupancy explicit.

Tests: allowance-one competitors never overlap, distinct execution IDs, pending cancellation after restart, uncertain external side effect not automatically repeated, management/log access during execution saturation. Exit: manual and scheduled Actions converge honestly without duplicate unintended execution.

### N3-D — Action UX and parity
Adapt CLI/MCP/Studio target selection internally while retaining user concepts of app/action/run/log/result. For migrated accounts, no user must provision a Hosted Runner solely to run an Action. Test workflows, file APIs, BYO routing/tunnels and disconnect behavior that N0 classifies as supported.

Exit: one schedule and manual success/failure/cancel journey run on managed and authorized BYO nodes. Unsupported behaviors have deliberate errors and documentation; they do not silently fall back to privileged execution.

## N4 — Canary, rollback and retirement

**Goal:** make Node execution the actual supported topology and remove superseded components safely.

### N4-A — Migration tooling and restore rehearsal
Implement a bounded resumable migration operation per account/host: inventoried -> quiescing -> fenced -> adopted -> verified -> active; blocked/failed remain observable. Use generation/transaction guards and explicit storage mappings. Inventory backup coverage for database, key material, artifacts, volumes and required configuration. Rehearse an application-consistent restore in an isolated environment before moving retained state.

Tests: interruption at every boundary, duplicate migration, old delayed command, restored database with stale ownership, resource overlap and partially applied policy. Exit: migration is repeatable; rollback compatibility is documented and tested. Before changing data formats, identify whether binary rollback is valid or coordinated restore is required.

### N4-B — Disposable canary
Start on a disposable account with static, stateful web app, build and scheduled/manual Action. Verify exact releases, volume identity/data, quota readback, routes, authentication, logs and reservations. Interrupt API/Worker/broker/Node in controlled fixtures; verify no unexplained data loss or repeated effects.

Stop on stale executor mutation, unauthorized access, quota escape, missing result, unrecoverable data or loss of management under saturation. Preserve diagnosis evidence. Exit: canary and rollback pass the finite acceptance matrix.

### N4-C — Retained-account cutover
Use authorized migration windows, backup checkpoints and one account/host cohort at a time. Stop old executor and prevent resurrection before transferring ownership. Verify supported tunnels/files/workflows and stored execution references. Resume admission only after readback and application-level probes. Record previous/new releases, generations, mounted data identities and cleanup debt.

Rollback restores a single executor owner with a new fenced generation; never run old and new managers concurrently over the same resources. Exit: managed cohort and one representative BYO installation accepted, with limitations documented.

### N4-D — Retire old topology
Remove per-account Hosted Runner provisioning, image/service dependencies, obsolete credentials/queues, duplicate dispatch paths and dead UI/settings after an agreed rollback window. Separately consolidate/remove the old Infrastructure Runner service when all host-administration responsibilities have migrated. Stop/remove containers without deleting their persistent volumes as a side effect.

Retain necessary historical identities through explicit migration/read models; no permanent dual executor fallback. If an unmigrated supported BYO cohort remains, isolate and name its temporary compatibility scope, tests, owner and exit condition. Do not claim platform-wide retirement while it remains.

Tests: new accounts create no Hosted Runner; migrated app/Action never targets retired executor; revoked identities cannot return; uninstall preserves data by default; no foreign-resource deletion. Update Admin, install, architecture, CLI and product documentation and dependency tests. Exit: no redundant installed management service or obsolete live execution path in the accepted scope.

## P1 — Useful platform, measurements and learning record

Reuse the former S05 three reference workloads and portable installation exercise after N4. Complete the minimal resource-status UI before expanding user access; broader M3 history/charts only if actual usage justifies them.

Measure old versus new idle memory/CPU/process count, 0/1/10 accounts, static density, app/build/Action active footprint, queue latency, storage/image/log growth, restore time and operational intervention. Use the same hardware/software/workload and label unavailable comparisons. Do not invent savings.

Cost model: monthly total = host invoices + backup/traffic/other services + valued operator time. Capacity is the tightest CPU/memory/storage/concurrency constraint after management and safety reserves. Allocated workload cost uses an explicit allocation rule; marginal cost differs from fixed fleet cost. Report idle account overhead removed, Node overhead introduced and new operational costs. No promise that one agent makes application memory disappear.

Deliver reproducible CLI examples, architecture ADR, failure-test report, benchmark/cost sheet and a concise engineering case study suitable for a CV. Success can be personal usefulness or research evidence; paid launch is optional.

## P2/P3 and R1/R2 — Later commitments

P2 reuses S04 recovery/backup/security work for the new topology, ranked by observed risk. Before offering production reliability to external users, demonstrate restore, revocation, bounded recovery and documented locality/support limits. Automatic host-loss failover, ARM/Windows, offline control-plane installations and distributed storage require separate scope decisions.

P3 reuses S06/S07 billing/pilot briefs after updating Hosted Runner references to Node placement/applied policy. Reconcile provider events with existing authority, preserve data on downgrade/unpaid states, validate commercial limits in test mode and base prices on measured costs. No automatic commercialization.

R1 reuses S08 workerd/V8 lab comparisons; R2 reuses S09 microVM comparisons. Node runtime extension stays a narrow capability proposal, not a plugin framework built before a second engine exists. A successful experiment needs a separate integration brief with security, image/kernel lifecycle, network/storage, operations, admission, cancellation and rollout requirements. Stronger isolation required by an accepted threat model cannot be waived for economy.

## Detailed slice briefs

All N0–N4 subsection briefs are available in the [Node execution index](node-execution/README.md), with a shared [execution contract](node-execution/EXECUTION-CONTRACT.md). Start with [N0-A](node-execution/N0-A-responsibility-audit.md). Later briefs are authored now; their dependency gates and code-level specification steps must be satisfied from actual preceding findings before implementation.

## Workflow for Luna-sized implementation

Give Luna this plan's global boundaries, ONE named slice and the handoff below. Each slice is a maximum scope; split further when it spans several independently reviewable changes. Audits do not authorize implementation, and implementation does not authorize production migration. Follow session authorization and repository deployment rules; do not introduce an extra confirmation for already authorized reversible work.

1. Read current checkout instructions and evidence; identify reuse before edits.
2. Implement one cohesive behavior with meaningful failure/concurrency coverage.
3. Run repository-required analyzer/build checks and affected tests. Never weaken gates.
4. Update canonical status and relevant Admin/architecture documentation.
5. Obtain independent review for tenancy, privileged execution, accounting and migration.
6. Release only within explicit task authorization; record code, test, deployed and live-accepted states separately.
7. Choose the next slice from actual evidence.

### Copyable first task

> Execute N0-A ONLY from the revised Node-first plan in the existing rzvdaniel/automaton checkout. Read AGENTS.md, matching skills, this master plan, the detailed N0-A brief and RM/runtime contracts. Verify branch/HEAD/worktree and current merged/deployed status. Inventory every Infrastructure Runner and Hosted Runner responsibility, durable identity, privilege, storage relationship and supported BYO behavior. Produce the responsibility map, factual unknowns and next smallest N0-B task. Do not implement, deploy, merge or mutate production.

### One-slice handoff contract

Return:
- Baseline: branch/HEAD/worktree, source/test pointers and observed support scope.
- Existing implementation reused, actual gap and exact scope.
- Changed files and rationale.
- Tests: commands/results/skips and required analyzer/build result.
- Failure analysis: duplicates/crash/stale generation/auth/quota/cancel/partial state.
- Data and compatibility impact; rollout/rollback bounds.
- Untested scenarios, required host/live evidence and remaining blockers.
- Canonical milestone disposition and next smallest slice.
- Commit/PR only if actually created.

## Technical references to verify during implementation

- Docker daemon privilege/security: https://docs.docker.com/engine/security/
- cgroup hierarchy/controllers: https://www.kernel.org/doc/html/latest/admin-guide/cgroup-v2.html
- Docker cgroup-parent and daemon configuration: https://docs.docker.com/reference/cli/dockerd/
- Historical roadmap: retrieve `docs/roadmap/managed-platform-completion-plan.md` from Git history; it is absent from current main.

The references support host-security/resource mechanisms, not an assertion that Automaton currently enforces every proposed boundary.

## Archived reference material

Historical briefs are preserved separately in [Automaton_Archived_Reference_Backlog.md](Automaton_Archived_Reference_Backlog.md). They are not active implementation instructions. The Node-first stages above govern all work; consult the archive only when a deferred requirement is deliberately reopened.
