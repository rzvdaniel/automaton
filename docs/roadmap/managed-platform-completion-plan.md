# Automaton hosting and platform completion roadmap

Updated: 29 September 2026. Latest live evidence: production acceptance run on 29 September 2026.

## Purpose and scope

Deliver a simple hosting product: people develop web applications on their own computers, using their preferred tools and AI assistants, then deploy through Automaton CLI/MCP and manage them in Studio. Existing organization membership, roles and permissions remain part of this product.

Finish subscription-derived resource limits and accounting, simplify Runner management, and complete the supported deployment, billing and recovery flows. **Hosted Builder and AI job sandboxes are future functionality, not prerequisites for this release.**

This is the current execution roadmap. It supersedes the sequence and status in the [earlier architecture assessment](../architecture/managed-platform-completion-plan.md) and the earlier discussion/downloadable plans. M0–M7 retain their meanings; RM is the separate Runner management simplification milestone.

Repository baseline: `main` at `295599d7`. This revision uses the repository plans, [M0/M1 inventory](../architecture/managed-platform-m0-acceptance-inventory.md), and recorded live checks. Reassess each milestone against then-current code before implementing it; a listed requirement does not mean its mechanism is absent.

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
| M1 storage implementation | Merged; **live acceptance complete (2026-09-29)**. The disposable app flow below completed and the app was removed through Studio. |
| Live routing and Runners | API commit `bb5c5a38` is live on green (image digest `sha256:e744b7c88193a5e18c5a7edb9a1949a38871cc8880b98571b0b63351030446e9`); `prod-verify.sh` passed. The blue rollback color remains healthy. Both Runners drained from blue and reconnected to green. Infrastructure Runner and Hosted Runner use `rzvdaniel/automaton-runner@sha256:77d29c25b61b0aae953233db5ab0e58b7ddc3badb3581377bbd6d0d4a45435b2`. |
| Fresh storage verification | Healthy `InfrastructureRunnerVerified` result: 0.00 GB / 2.00 GB, measured at 5:17 PM on 2026-09-29 after the app wrote its marker. This tiny marker sample verifies the flow, not large-write accounting. |
| Runtime restrictions | The fixture container ran as UID/GID `1000:1000`, had a read-only root filesystem, `/tmp` tmpfs capped at 32 MiB, and a writable `/automaton/data` mount. These are observed fixture settings, not new tier defaults. |
| Persistence and cleanup | The app process wrote and read `m1-marker.json`; after Hosted Runner reprovision it read the same marker. Studio then deleted the app. The Runner host had no matching app container; the marker and app-specific data directory were absent. The Hosted Runner user volume remained. |
| Earlier upload failures | Earlier deployments on the retiring SignalR path timed out. The released RabbitMQ/HTTPS path subsequently completed CLI deployments, including this fixture after the successful API slot cutover. The in-flight old-slot command retry case remains RM acceptance work. |
| Worker provenance | Running image is `automaton-prod-worker:latest`, digest `sha256:6bb80ed06b60f96dd1fe7d541bd61851bca11336ce28d1c6c13f0ef0d53d151f`, created 2026-09-28 07:45:37 UTC. Its image has no source-revision label, so the source commit remains unknown. This is a separate release-tracking gap; the Worker does not perform the failed API-to-Runner deployment-package transfer. |
| Failed deployment trace | The historical `File upload timed out` operation remains evidence for why RM was started; the available trace did not distinguish chunk delivery from callback delivery. The replacement deployment path now succeeds. |
| RM | Hosted Runner deployment commands, HTTPS package retrieval/results, and HTTPS storage telemetry are released. On 2026-09-29, a CLI deployment operation started while API traffic was blue, remained in progress during the normal cutover to green, and reached terminal package-deployment success after the cutover; Studio recorded the Live deployment as Running and the app endpoint returned its expected response. The package-backed app container was already running when the route changed, so this run does **not** prove package retrieval after the switch. Cross-host package availability, retry/lost-callback live recovery, and removal of the remaining replaceable SignalR management callers remain open. |
| M2–M7 | Remaining completion/acceptance scope; audit existing mechanisms before adding code. |
| Builder | Future functionality; outside this release's completion gates. |

Runner image digest observed in production: `sha256:77d29c25b61b0aae953233db5ab0e58b7ddc3badb3581377bbd6d0d4a45435b2`.

The disposable `M1 Live Acceptance 20260928` app was deleted after its checks passed. Studio reported no apps; the Runner host had no matching container, and the marker/app-specific data directory were absent. Host scheduling was verified enabled. The isolated XFS test used a placeholder Infrastructure Runner and does not prove live control-plane reachability during disk exhaustion. A single Hosted Runner startup warning said managed application container reconciliation could not be retried from `deployment.json`; the app remained running and passed marker readback after reprovision. Track this warning if it recurs.

## Execution order

1. The historical SignalR upload failure led to RM; its replacement RabbitMQ/HTTPS path completed production CLI deployments. The Worker image's unknown source revision remains a separate release-tracking gap.
2. M1 live acceptance is complete. Continue RM with the in-flight cross-slot retry test, cross-host package availability assessment, and removal of replaced SignalR management callers. Do not add a new storage system solely for the first package path.
3. Close RM only after its remaining cutover/cleanup acceptance passes; M1 no longer blocks it.
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

## M1 — Finish storage acceptance (**live acceptance complete 2026-09-29**)

Retain the implemented storage model and review coverage against the M0/M1 inventory:

- Combined system/user XFS project assignment, inheritance and hard limits, including app data, build workspaces, artifacts and caches.
- Read-only app/build roots, bounded temporary storage and shared memory, and safe runtime UID/GID access to persistent data.
- Separately bounded Hosted Runner writable layer, Docker logs and shared image/cache storage. The fixed Docker filesystem is a combined host capacity limit, not a separate tenant quota or image-only allowance.
- Explicit limits and retention for control-plane uploads, source/version copies, published assets and media outside the account XFS domain. Identify remaining per-process admission gaps under multiple API replicas.
- Cleanup of failed/cancelled staging and disposable output while preserving durable data and the working deployment.

The 2026-09-29 live acceptance flow completed:

1. Released API `bb5c5a38` and Runner image digest `sha256:77d29c25…` were active. The API blue/green deployment drained both active Runners to green; production health checks passed.
2. The disposable app deployed through the CLI and reported terminal success with `/health` reachable.
3. The app process, running as UID/GID `1000:1000`, wrote and read its marker from `/automaton/data`.
4. Fresh host-side storage verification reported Healthy, `InfrastructureRunnerVerified`, 0.00 GB / 2.00 GB after the marker write.
5. Hosted Runner reprovisioning completed. The app remained running with read-only root, bounded `/tmp`, and the writable data mount; its process read back the same marker.
6. Studio deleted the disposable app. Studio showed no remaining apps, the Runner host had no matching container, and the marker and app-specific data directory were absent. The Hosted Runner user volume remained. Host scheduling was verified enabled.

The M1 live acceptance is complete. Previously recorded isolated XFS results remain limited to their documented scope; the placeholder Infrastructure Runner did not prove management reachability during shared-filesystem exhaustion. Broader service recovery under exhaustion remains M6 work.

## RM — Simplify Runner management and remove replaced SignalR paths

Use the existing RabbitMQ infrastructure for durable management commands and authenticated HTTPS for artifacts/results. Keep stable Runner identity independent of API instances. Inventory existing command consumers and callbacks first; extend them where their contract fits.

### Current code audit (`main` at `295599d7`)

- Hosted Runner deployment operations are persisted by the API and dispatched as durable RabbitMQ commands addressed to the Runner identity. Each Hosted Runner has restricted broker credentials; the Runner retrieves the package and posts execution results through authenticated HTTPS. The Runner journal and API operation state make retries recoverable and prevent a persisted successful host action from being applied again. The path has no SignalR deployment fallback.
- Infrastructure Runner trusted actions also use the durable command queue and authenticated HTTPS secret/result APIs. Infrastructure Runner host-validation scripts and several separate Runner operations still use SignalR; do not conflate these remaining calls with the released deployment-command path.
- The Worker is not in the API-to-Runner deployment-package transfer path. Its live image digest and unknown source commit remain a separate release-tracking gap.
- Deployment packages are staged under API `AppData/deployment-operations`. Production blue and green API services share the `prod-api-appdata` Docker volume on this host, so the same-host slot pair can read a shared staged package. Availability when API slots run on different API hosts is **not established**; do not add a new storage system for this first path.
- `AutomatonHub` still carries workflows/custom steps, app actions and action administration, managed-artifact synchronization, app file/media transfer, host-container execution, reconnect/drain coordination, and runtime tunnels. Storage telemetry now uses authenticated HTTPS endpoints. Retain independent features such as Studio live updates or BYO tunnel behavior if they still require SignalR, and classify each remaining caller before removal.

The storage telemetry API resolves the Runner from its API-key identity, validates its purpose and Hosted Runner/Runner Host association, then submits the report through the existing telemetry service. The Runner posts over HTTPS with its existing API key (HTTP is allowed only for loopback development). This removes storage-measurement callbacks from `AutomatonHub`; it does not complete RM or change storage accounting/enforcement.

The released deployment path uses stable Runner/operation identity, per-Hosted-Runner broker credentials, bounded authenticated HTTPS package retrieval, and a persisted result callback. The package path uses the existing shared volume, with cross-host availability still unestablished. Expand it to other replaceable management callers, then remove their old transports and registries during coordinated cutovers.

### Required contract

- Address commands by stable Runner/operation identity, never a SignalR `ConnectionId` or API color. Use scoped broker credentials and permissions; tenant runners must not receive Infrastructure Runner authority.
- Hosted Runners receive an individual RabbitMQ user limited to consuming their durable deployment queue. The API provisions the queue/user through the configured RabbitMQ Management API; production must provide `RabbitMq.ManagementUrl` and the Hosted Runner broker endpoint.
- Successful Hosted Runner deprovision deletes that RabbitMQ user and queue. RabbitMQ user deletion revokes its credentials and closes connections authenticated as that user; a failed deprovision does not claim successful revocation.
- Persist operation intent, publish reliably and make execution retry-safe. Use publisher confirmation, appropriate durable queues/messages and consumer acknowledgement only after recoverable execution/result state exists. Reuse established result-before-ack behavior where applicable.
- Handle duplicate delivery and a crash after host-side success without repeating destructive work. Reconcile uncertain outcomes; do not promise exactly-once transport.
- Transfer artifacts through bounded authenticated HTTP requests/downloads with ownership checks, expiry, checksums and cleanup. The first path uses the existing package volume; availability across different API hosts is not established and must not be implied by same-host blue/green results. Do not add a new storage system solely for this path.
- Deliver results idempotently through authenticated APIs, retrying safely after API outages. Commands reference the intended trusted action/artifact revision so stored stale modules cannot silently execute.
- Keep one connection/recovery lifecycle with bounded backoff and jitter. RabbitMQ still has connections and broker recovery; the simplification removes dependence on API deployment connections, not all reconnects.
- Existing apps keep running during API/Worker deployments. New management work may wait while a dependency is unavailable; expose that state truthfully.

### Cleanup in the same milestone

Inventory every Runner SignalR caller before removal. Delete the replaced management hubs/clients, file-transfer paths, connection registries, cutover acknowledgements, reconnect/drain coordination, settings and obsolete tests. Remove the requirement for all Runners to register on a new API slot before API traffic can switch.

Retain independent supported features, such as Studio live updates or BYO tunnel functionality, if they still require SignalR. Name those remaining uses in the final inventory. No management fallback to the retired transport; update runbooks and Admin documentation to match the implemented behavior.

**First vertical-path acceptance:** a CLI deployment command survives Runner retry; the Runner retrieves its package after an API slot switch using authenticated HTTPS; the operation reaches a persisted terminal state without a SignalR completion callback. Treat the existing shared package volume as usable for the tested same-host slot arrangement only. Availability when the two slots are on different API hosts remains unestablished; do not add a storage system for this first path.

Automated regression coverage checks that an active API service instance can serve a package staged before a simulated slot switch, and that a Runner retry after a persisted successful result does not apply the runtime again. These unit tests cover shared-path and journal behavior.

On 2026-09-29, live acceptance started a CLI deployment while API traffic was on blue, then ran the standard API redeploy/cutover to green while the app process remained in a deliberate startup delay. The cutover drained the previous API color and the Runner reconnected; the CLI later reported `packageDeploy` pass and “Deployment package applied,” Studio recorded a running Live deployment, and the deployed app returned its expected response. An initial attempt cut over before the Runner was ready and ended with a `Runner is not connected` warning; it was not counted as a pass. The successful run proves that an in-flight command reached terminal success across a same-host API slot switch. Since the app container was already running at the time the successful cutover changed traffic, this evidence does **not** establish that the Runner fetched the package after the switch. No cross-host package test was performed.

M1's separate disposable app checks are complete. RM's package-after-switch, retry/recovery, and remaining SignalR cleanup acceptance stays open.

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

**Next action:** complete focused automated validation and review of the first Hosted Runner command/package-transfer path, then release it for the stated CLI acceptance. Keep the disposable app until app-originated marker read/write, usage measurement, reprovision and supported cleanup all pass. Track Worker source provenance separately. Do not start Builder or expand commercial promises to work around the pending acceptance.
