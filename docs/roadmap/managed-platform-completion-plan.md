# Automaton hosting and platform completion roadmap

Updated: 29 September 2026. Latest live evidence: operator report from 28 September 2026.

## Purpose and scope

Deliver a simple hosting product: people develop web applications on their own computers, using their preferred tools and AI assistants, then deploy through Automaton CLI/MCP and manage them in Studio. Existing organization membership, roles and permissions remain part of this product.

Finish subscription-derived resource limits and accounting, simplify Runner management, and complete the supported deployment, billing and recovery flows. **Hosted Builder and AI job sandboxes are future functionality, not prerequisites for this release.**

This is the current execution roadmap. It supersedes the sequence and status in the [earlier architecture assessment](../architecture/managed-platform-completion-plan.md) and the earlier discussion/downloadable plans. M0–M7 retain their meanings; RM is the separate Runner management simplification milestone.

Repository baseline: `main` at [69e11d04](https://github.com/rzvdaniel/automaton/commit/69e11d04d54cedf76461bc6000ed2aec671bcc39). This revision uses the repository plans, [M0/M1 inventory](../architecture/managed-platform-m0-acceptance-inventory.md), and the latest operator report. Live results below were reported, not independently rerun for this document. Reassess each milestone against then-current code before implementing it; a listed requirement does not mean its mechanism is absent.

## Product boundaries

| Area | Initial direction |
| --- | --- |
| Development | Local editor and AI tools; CLI/MCP deployment through supported API capabilities. Existing server-side builds remain bounded where needed. |
| Static sites | Shared serving of published files, with a URL per app and explicit ownership, access, storage and traffic limits. No dedicated process or continuously running Hosted Runner solely to serve static files. Audit and reuse the existing publishing path. |
| Dynamic apps | Managed runtime containers under one account resource budget, or existing bring-your-own Runner support. |
| Free offering | Prioritize bounded static hosting and BYO. Keep managed dynamic hosting restricted to an explicitly limited trial/cohort until measured economics justify wider availability. Do not allocate an always-running Hosted Runner merely because someone signs up. |
| Paid offering | Explicit aggregate CPU, memory, PID and storage allowances, plus limits on concurrently running apps and build/action concurrency. Saved apps are not an unlimited entitlement to running processes. |
| Organizations | Reuse existing organization code; verify authorization across CLI, MCP, API and Studio. This roadmap does not silently change account resource ownership into organization billing. |
| Deferred | Hosted Builder, AI sandboxes, automatic sleep/wake, autoscaling, multi-region failover and automatic live storage migration. |

These are product decisions to implement and validate, not statements that every path already behaves this way. Exact tier sizes, prices, bandwidth allowances and retention periods are decided in M7 from measurements; historical Free defaults are not approved production promises.

## Working rules

- Keep focused changes and reuse existing policies, identities, durable operations and storage helpers.
- Use one server-authoritative subscription policy; distinguish requested policy from confirmed enforcement.
- No legacy fallback or parallel old/new Runner management protocols. Coordinate the cutover and remove replaced code. Preserve persistent data and stable identities.
- Durable state must support multiple API/Worker replicas, retries and process failure. Do not introduce speculative locks, new orchestration frameworks, Kubernetes or distributed storage.
- Keep legitimate operation deadlines and bounded recovery. Remove arbitrary sleeps and API-instance registration as deployment success dependencies.
- Use immutable release artifacts and record deployed provenance. Runner releases continue through the established pipeline.
- Separate implementation, automated tests and live acceptance. Never mark an untested guarantee complete.

## Current position

| Milestone / evidence | Status |
| --- | --- |
| Historical Stages 1–3 and M0 | Reported complete; retain account identity, atomic admission and storage locality protections. |
| M1 storage implementation | Merged; **live acceptance pending**. |
| Live routing and Runners | API `69e11d04` reported live on green; disposable app `/health` reachable. Both Runners reported on build #4864, source `ee4babc4`. |
| Fresh storage verification | Healthy `InfrastructureRunnerVerified` result: 126,976 user bytes and 0 system bytes. One sample does not prove all write-path coverage. |
| Runtime restrictions | App root read-only; `/tmp` bounded to 32 MiB; persistent data writable with the intended UID 1000. These are observed fixture settings, not new tier defaults. |
| Persistence | A marker written directly as UID 1000 survived two Hosted Runner reprovisions. Application-originated write/read remains unproven. |
| Remaining live blocker | Deployment failed three times with `File upload timed out`; the source change selecting the intended runtime user was not deployed. |
| Worker provenance | Exact revision of the running `automaton-prod-worker:latest` image was not established. Missing labels alone do not prove it is outdated. |
| RM | Proposed; implementation not started. |
| M2–M7 | Remaining completion/acceptance scope; audit existing mechanisms before adding code. |
| Builder | Future functionality; outside this release's completion gates. |

Reported Runner digest: `sha256:84cfa3ca55578b8f15ae4919d04ebddd5391704825543f4368187f3ea09f95bd`.

Keep the disposable `M1 Live Acceptance 20260928` app until remaining checks finish, then clean it up through the supported lifecycle. Scheduling was last reported enabled; record its actual state for each maintenance operation. The isolated XFS test used a placeholder Infrastructure Runner and does not prove live control-plane reachability during disk exhaustion.

## Execution order

1. Diagnose one failed upload end to end and preserve the existing M1 evidence. Apply a small justified fix if sufficient, then finish M1.
2. If completing the upload would require substantial work on the SignalR transfer path being replaced, implement RM and carry the remaining M1 checks into its acceptance. M1 remains pending until those checks pass; avoid an open-ended repair of the retiring transport.
3. Complete RM, including removal of replaced SignalR management and a successful CLI deployment with application-originated persistence checks.
4. Complete M2 and M3. Collect M5/M6 lifecycle and recovery evidence alongside this work.
5. Complete M4 and the remaining M5/M6 gaps for supported hosting modes.
6. Close M7 before enabling the corresponding paid promises. A restricted development cohort may precede broad launch, with explicit limits and known gaps.

Address security or data-loss defects when discovered. Builder is not on this critical path.

## M0 — Preserve the account resource contract

- `ApplicationUser.Id` remains the resource owner, with `AccountStorageAllocation` owning the stable project identity.
- Retain one active managed Hosted Runner per account and atomic admission across normal, admin and recovery paths. Do not bypass existing count semantics during this work.
- Retain one authoritative account storage location on one verified XFS filesystem. The same project ID on two filesystems does not create a shared quota or move data.
- Keep the Hosted Runner, its managed apps and builds within the intended account cgroup hierarchy. The Infrastructure Runner stays outside it.
- Preserve admission, ownership and locality checks. Existing conflicting history requires explicit resolution; account hard deletion remains blocked until safe storage teardown exists.

**Exit discipline:** later milestones must preserve these contracts rather than reopen the resource-owner design.

## M1 — Finish storage acceptance

Retain the implemented storage model and review coverage against the M0/M1 inventory:

- Combined system/user XFS project assignment, inheritance and hard limits, including app data, build workspaces, artifacts and caches.
- Read-only app/build roots, bounded temporary storage and shared memory, and safe runtime UID/GID access to persistent data.
- Separately bounded Hosted Runner writable layer, Docker logs and shared image/cache storage. The fixed Docker filesystem is a combined host capacity limit, not a separate tenant quota or image-only allowance.
- Explicit limits and retention for control-plane uploads, source/version copies, published assets and media outside the account XFS domain. Identify remaining per-process admission gaps under multiple API replicas.
- Cleanup of failed/cancelled staging and disposable output while preserving durable data and the working deployment.

Finish the live flow:

1. Establish deployed API/Worker/Runner provenance and trace one upload failure through its operation ID, transfer and acknowledgement.
2. Deploy the disposable fixture through the supported CLI/API flow; require terminal `Succeeded` and reachable `/health`.
3. Have the application process write and read a marker through its declared persistent data path. A host-side write does not satisfy this check.
4. Measure a known bounded persistent write through fresh authoritative storage verification.
5. Reprovision using supported operations; confirm identity, volume contents, effective bounds and application-originated marker readback.
6. Delete the disposable fixture and verify intended cleanup without prematurely releasing the account allocation. Record final scheduling state.

**Exit:** recorded host tests establish quota coverage and exhaustion behavior; the real platform flow proves upload/deploy, app-originated persistence, measurement, reprovision and cleanup. Any missing category stays explicitly open. Broader service recovery under exhaustion belongs to M6.

## RM — Simplify Runner management and remove replaced SignalR paths

Use the existing RabbitMQ infrastructure for durable management commands and authenticated HTTPS for artifacts/results. Keep stable Runner identity independent of API instances. Inventory existing command consumers and callbacks first; extend them where their contract fits.

### Required contract

- Address commands by stable Runner/operation identity, never a SignalR `ConnectionId` or API color. Use scoped broker credentials and permissions; tenant runners must not receive Infrastructure Runner authority.
- Persist operation intent, publish reliably and make execution retry-safe. Use publisher confirmation, appropriate durable queues/messages and consumer acknowledgement only after recoverable execution/result state exists. Reuse established result-before-ack behavior where applicable.
- Handle duplicate delivery and a crash after host-side success without repeating destructive work. Reconcile uncertain outcomes; do not promise exactly-once transport.
- Transfer artifacts through bounded authenticated HTTP requests/downloads with ownership checks, expiry, checksums and cleanup. Artifact availability must survive API replica changes; a URL pointing only to one replica's local disk is insufficient.
- Deliver results idempotently through authenticated APIs, retrying safely after API outages. Commands reference the intended trusted action/artifact revision so stored stale modules cannot silently execute.
- Keep one connection/recovery lifecycle with bounded backoff and jitter. RabbitMQ still has connections and broker recovery; the simplification removes dependence on API deployment connections, not all reconnects.
- Existing apps keep running during API/Worker deployments. New management work may wait while a dependency is unavailable; expose that state truthfully.

### Cleanup in the same milestone

Inventory every Runner SignalR caller before removal. Delete the replaced management hubs/clients, file-transfer paths, connection registries, cutover acknowledgements, reconnect/drain coordination, settings and obsolete tests. Remove the requirement for all Runners to register on a new API slot before API traffic can switch.

Retain independent supported features, such as Studio live updates or BYO tunnel functionality, if they still require SignalR. Name those remaining uses in the final inventory. No management fallback to the retired transport; update runbooks and Admin documentation to match the implemented behavior.

**Acceptance:** deploy the API while a Runner is working; restart Worker and Runner; exercise duplicate commands, lost callbacks, broker interruption and recovery; verify tenant isolation and correct artifact/action revision. Show that a completed operation remains completed and running apps remain healthy. Test reconnect recovery at a stated cohort size; a small test does not establish thousand-Runner capacity.

**Cutover:** pause new management work, drain or explicitly resolve old in-flight operations, deploy coordinated versions, then resume. Preserve data and identities. Close RM only after the CLI flow and any carried M1 checks pass.

## M2 — Compute, running-app admission and capacity

- Read back aggregate CPU, memory and PID limits after create/restart/reprovision. Verify Hosted Runner, app and build membership in the same intended cgroup.
- Exercise simultaneous workload saturation, OOM and PID exhaustion; preserve Infrastructure Runner access and reserve host/control-process headroom.
- Bound build/action concurrency and apply backpressure. Local development reduces platform build demand but does not eliminate server-side workload limits.
- Distinguish saved app count, assigned deployment targets and concurrently running dynamic apps. Retain existing constraints until an explicit policy migration replaces them; introduce server-enforced running-app admission where needed.
- Include pending creates/resizes, failures, cleanup and stale telemetry in concurrency-safe host reservations. CPU sharing must be explicit; disk capacity does not justify overcommitting memory.
- Shared static serving must not reserve one dynamic runtime or Hosted Runner per site. Apply separate shared-service capacity limits.

**Exit:** combined workloads cannot escape the configured account budget; concurrent start/placement cannot exceed the reservation policy; failed operations release reservations safely. Existing admission paths all enforce the same policy.

## M3 — Effective limits and usage visibility

- Expose desired/applied policy, runtime and build concurrency, authoritative disk usage, sample time and verification status through API and Studio.
- Identify disk samples by filesystem/storage domain and project ID; do not sum duplicate reports of the same project.
- Show stale, unavailable and error states explicitly. Unknown usage is not zero and cannot authorize unsafe shrinking or placement.
- Separate account quota, separately limited control-plane/static storage and platform overhead. Explain included categories and logical versus allocated bytes.
- Show saved versus running apps and actionable quota/admission errors. Bound telemetry retention and report verification/reconciliation lag.

**Exit:** customer-visible limits match confirmed enforcement, and delayed/duplicate reports cannot corrupt authoritative state.

## M4 — Safe policy changes and billing

- Route administrator assignments, specification changes and paid subscription changes through one durable resource transition with requested, applying, applied, blocked and failed outcomes.
- Keep the applied snapshot until host confirmation. Preserve identity and data during upgrades/downgrades; require fresh usage for shrink decisions and an explicit over-limit/grace policy.
- Retain shared-project conflict guards until any coordinated replacement is proven safe.
- Reconcile billing against current provider state; handle duplicate/out-of-order events, concurrent checkout, payment failure/recovery, cancellation, suspension and resumption.
- Keep payment status separate from resource-application status. Make Free/cancel actions functional and compare stable plan identities.

**Exit:** provider test-mode scenarios and host policy acceptance show correct convergence without silent data deletion or advertising resources not yet applied. Final amounts and commercial terms belong to M7.

## M5 — Complete the supported app lifecycle

Use a finite matrix for static publication, managed web apps, actions-only apps and applicable BYO flows. Reuse existing organization authorization; Builder introduces no requirements here.

| Flow | Acceptance |
| --- | --- |
| Create/import/clone/remix | Correct ownership and admission; portable source and attribution preserved; no copied secrets, durable runtime data or Runner assignment. |
| CLI/MCP upload and build | Bounded authenticated inputs, durable status, cancellation and quota/OOM errors; failed attempts clean up without removing the working version. |
| Static publish/update | Own URL through shared serving; only intended published files exposed; access and aggregate storage limits hold across replicas. |
| Dynamic deploy/update | Immutable candidate, readiness, durable promotion, routes and authorization; recover crashes before/after promotion. Define stateful overlap and rollback limits. |
| Stop/start/unexpected exit | Desired/observed state converges; routes and running-app reservations agree; restart loops are bounded. |
| Reprovision/host change | Preserve identity and data; verified transfer/restore or explicit blocked relocation. Reprovision alone is not migration. |
| Unpublish/undeploy/delete | Clear data-retention scope, offline cleanup retry and ownership checks; cleanup debt remains visible. |
| Organization access | Existing roles correctly authorize deployment, management, secrets and deletion across API, CLI/MCP and Studio. |

**Exit:** supported matrix rows pass; CLI deployment success means terminal success and required reachability checks. Mark inapplicable cases explicitly. New app products and runtime engines remain separate scope.

## M6 — Infrastructure recovery and operations

- Reuse RM recovery evidence and complete multi-API/Worker restart/partition tests, bounded retries, poison-command handling and operator retry/cancel.
- Verify enrollment/revocation, readiness, maintenance admission, drain, restart and failed reprovision recovery.
- Back up control-plane metadata and durable app data, with required keys/configuration and consistency rules. Restore into a replacement environment and measure agreed RPO/RTO.
- Respect host-local data during replacement: verified restore/transfer or an explicit placement block. No implied automatic failover.
- Add safe account storage teardown before allowing hard deletion of allocations with retained data.
- Exercise recoverable low-disk conditions with the real management path in a suitably isolated environment; never fill the filesystem serving unrelated apps to prove a point.
- Monitor queue age, failed operations, host/storage freshness, watermarks, crash loops, backup age, cleanup debt and billing/resource drift.
- Verify tenant authorization, scoped host access, path/symlink protection, secrets redaction/rotation and bounded uploads/logs.
- Record release provenance and exercise rollback/roll-forward with preserved data. Keep procedures proportionate to a small operated fleet.

**Exit:** documented drills restore service/data within chosen targets, and operators can detect and resolve critical failures. A running container or a single quota test is not a resilience certification.

## M7 — Economics, pricing and release

- Measure representative static sites, idle/active dynamic apps, concurrent builds and Hosted Runner overhead on actual hardware. Establish safe host reserves and utilization rather than estimating users from disk size.
- Model Free cohort costs, compute, persistent/control-plane storage, backups, shared images/logs, traffic, payment fees and operational effort.
- Choose tier resources, saved/running app limits, build/action concurrency, storage/traffic quotas, retention and support commitments. Make Free sustainable without promising unlimited always-on compute.
- Align plan/specification configuration, payment catalog, API, Studio, website and Admin documentation. Hidden/test plans must not be purchasable through direct calls.
- Verify that static hosting does not keep a tenant runtime alive merely for serving. Audit existing publication first; keep missing work within M2/M5 rather than creating a new hosting engine by default.
- Release through a limited cohort with observable stop/rollback criteria. Enable paid offerings only after M1–M6 evidence supports the guarantees sold.

**Exit:** users can understand, receive, observe, change and cancel the advertised service, and measured costs support its limits. Do not publish server capacity as a guaranteed user count without representative workload data.

## Future functionality — Hosted Builder and job sandboxes

Revisit after the local-development-and-deployment product is reliable and demand justifies the additional execution surface. Builder remains a normal, forkable Automaton app using generic authorized platform capabilities.

- Keep the two existing Hosted Runner persistent volumes: system state and user workspace. Reuse canonical naming and path helpers; introduce no persistent volume per Builder project or job.
- Apps receive only their own authorized user-volume subpaths. The system volume is never mounted into apps or jobs; Builder receives no Docker socket or privileged host access.
- Builder owns projects and agent state in its own persistent app data directory, conceptually `workspace/data/{builderAppId}/projects/` and `agent-state/`, respecting any configured data-directory prefix.
- A temporary job container receives only the selected project at `/workspace`, an explicitly scoped optional cache, and bounded scratch storage. Resolve ownership and paths on the server; prevent traversal, symlink escapes and competing writes to the same project.
- Job CPU, memory and PIDs share the existing account aggregate. Persistent projects/caches inherit the account quota; writable layers and tmpfs also require explicit bounds. Arbitrary package installation is not an entitlement to privileged execution.
- Jobs are sibling host containers under trusted platform control, not Docker-in-Docker. Removing a job preserves its project data.
- Generated apps deploy immutable artifacts and own separate app data. Their operation must not depend on Builder remaining alive. Forking copies permitted source, not secrets, runtime state or live assignments.

Before implementation, define a small job contract covering authorization, image policy, cancellation, deadlines, credentials/network access and cleanup. No generic workflow framework or AI subscription system is required for the current hosting release.

Automatic sleep/wake is also deferred: persistent storage and scarce compute can be separated, but safe wake routing, cold starts, concurrent requests and background work need a dedicated lifecycle contract.

## Tracking and handoff

Historical Stages 4–7 map to M1, Stage 8 to M2, Stage 9 to M3, and Stage 10 to M4. M5–M7 cover existing app lifecycle, operational recovery and commercial readiness. RM changes management transport; it does not replace resource enforcement or recovery acceptance.

For each milestone, record status, PR/source revision, deployed image digest, policy/configuration version, test environment, acceptance results and remaining gaps. Update this roadmap as evidence changes.

Use focused PRs and repository-required checks for the affected code. Update Admin guides when actual operator behavior changes. Documentation-only roadmap changes need link/content/whitespace checks, not application builds or deployments.

**Next action:** establish Worker provenance and trace one failed disposable-app upload, then follow the M1/RM decision above. Do not start Builder or expand commercial promises to work around the pending acceptance.
