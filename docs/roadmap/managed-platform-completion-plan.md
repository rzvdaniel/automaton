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
| RM | Hosted Runner deployment commands and HTTPS package retrieval/results are live in production. Storage telemetry's authenticated HTTPS implementation is merged in PR #326, but its live rollout was not verified in this run. On 2026-09-29, a command queued behind an active deployment fetched its package from the newly active API slot after the normal same-host blue/green cutover; the active API logged HTTP 200 for both the package GET and result POST, and the CLI reached terminal success. Cross-host package availability, live retry/lost-callback recovery, and removal of the remaining replaceable SignalR management callers remain open. |
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

### Current code audit (`main` at `2d261949`; Slice 1 changes below are awaiting review/release)

- Hosted Runner deployment operations are persisted by the API and dispatched as durable RabbitMQ commands addressed to the Runner identity. Each Hosted Runner has restricted broker credentials; the Runner retrieves the package and posts execution results through authenticated HTTPS. The Runner journal and API operation state make retries recoverable and prevent a persisted successful host action from being applied again. The path has no SignalR deployment fallback.
- Infrastructure Runner trusted actions also use the durable command queue and authenticated HTTPS secret/result APIs. Infrastructure Runner host validation now uses the same durable queue and authenticated HTTPS result path in this slice; several separate Runner operations still use SignalR; do not conflate these remaining calls with the released deployment-command path.
- The Worker is not in the API-to-Runner deployment-package transfer path. Its live image digest and unknown source commit remain a separate release-tracking gap.
- Deployment packages are staged under API `AppData/deployment-operations`. Production blue and green API services share the `prod-api-appdata` Docker volume on this host, so the same-host slot pair can read a shared staged package. Availability when API slots run on different API hosts is **not established**; do not add a new storage system for this first path.
- This RM slice moves trusted host-container execution (image resolution, builds, and app-container create/inspect/start/log/remove) from SignalR to the existing durable Infrastructure Runner RabbitMQ queue. The API persists an operation, addresses its Infrastructure Runner by stable identity, reapplies the existing Hosted Runner ownership, active deployment, resource snapshot, and host-storage admission checks, and accepts the result through an API-key-authenticated HTTPS endpoint. Operation IDs, API disposition checks, deterministic app/build container identities, and build-container reconciliation make delivery retries reuse or reconcile host work. Only completed build containers are removed after the result is persisted. PR #328 is merged and released. PR #330 result-delivery recovery and the focused live checks below are complete; broader RM acceptance remains open.
- Host-container execution, result-delivery recovery and host validation are migrated, released and verified (PRs #328–#333). Keep those areas closed unless a concrete regression appears. Workflows/custom steps are deployed; live execution checks are deferred to final RM acceptance. Slice 2 replaces App Action management below. Managed-artifact synchronization, file/media transfers, and reconnect/drain coordination remain. Retain independent Studio updates and supported BYO runtime tunnels.

### Five remaining RM slices

| Slice | Scope | Status |
| --- | --- | --- |
| 1 — Workflows and custom steps | Execution, cancellation intent, persisted results; remove replaced callers/handlers | Deployed; live execution checks deferred to final RM acceptance. |
| 2 — App actions | Execute/cancel, logs/results, module/version management and cleanup | Implemented in this PR; review, CI and release pending. |
| 3 — Managed artifacts | Application-artifact synchronization | Pending. |
| 4 — File transfers | App/media uploads/downloads and generic deployment transfer | Pending. |
| 5 — Connection cleanup | Obsolete management hub code, registries, reconnect/drain and API-slot registration gates | Pending; remove only after dependent callers move. |

Use focused regressions for each slice, followed by **one combined RM recovery/cutover acceptance run**. Carry deliberate host-validation interruption into that final run; it is not another milestone or a blocker for the next slice. RM is complete only when management callers have moved, obsolete connection coordination is removed and combined acceptance passes. M1 remains complete; M2 and later milestones are outside this work. Cross-host package availability remains an explicit limitation and separate scaling task; current API slots share storage on one control-plane host.

#### Slice 1 audit and implementation

The pre-edit audit found four API callers: `RunnerFlowExecutionService.ExecuteCoreAsync`, `ScheduleService.ExecuteFlow`, `CustomStepRunnerExecutionService.InvokeRunnerAsync` and `AppsStepExecutionService.ExecuteOnRunnersAsync`. They invoked `RunWorkflow` or `RunCustomStep` through a Runner `ConnectionId`. `HubExecutionHandlerRegistrar` handled both with the existing `FlowExecutionService` and `CustomStepExecuteService`.

These callers now share durable execution dispatch. Each invocation persists one `RunnerExecutionOperation` and an identity-only command in the existing transactional CAP outbox. The existing RabbitMQ publisher-confirmation pattern forwards it to the addressed Runner's durable execution queue. All supported Runner profiles can obtain a separate consume-only credential through API-key-authenticated HTTPS with the stable Runner identity header. Enrolled Runners require their bound API key; existing unbound BYO registrations retain owner-key authorization through the existing Runner access policy. The credential cannot publish, configure queues or consume another Runner's queue. Broker settings carry explicit TLS enablement and expected server name; workflow/custom-step issuance and consumption require TLS. Certificate-chain, hostname and revocation validation remain enabled, with OS trust and no plaintext fallback. The shared Hosted Runner deployment consumer also honors these TLS settings; rollout requires a configured TLS listener and Runner trust. Runner removal/revocation/deprovisioning closes unfinished operations and removes execution broker access.

The Runner fetches the pinned request over authenticated HTTPS and returns a terminal result over HTTPS. Requests and results are encrypted at rest with the existing API Data Protection service; API replicas must share its existing key ring. Scripts and decrypted inputs do not enter the broker. Actor/Runner membership and project access, Infrastructure Runner administrator checks, workflow capability, fresh-heartbeat admission, recurring scheduling and existing engine configuration remain in place. Migrated admission no longer requires a SignalR connection ID. Workflow loading and its existing HTTP history persistence remain unchanged; workflow/custom-step outputs and logs are returned through the persisted result. Existing artifact roots/synchronization are unchanged; replacing synchronization belongs to Slice 3.

A Runner journal records acceptance before execution and saves completed results atomically. Redelivery reuses that result, or the API's already-applied disposition. If acceptance survived but completion did not, the result is `Lost` with an explicit uncertain-work message; the body does not run again. Journal write failure preserves the computed result in memory for retry and does not turn successful work into a reported execution failure. Journals are cleaned only after the API confirms terminal persistence. Transient HTTPS failures reuse the computed result and honor `Retry-After`; known invalid commands terminalize their authorized operation before rejection, while unreadable messages with no identity are logged and discarded. The owning consumer scope handles teardown outside its delivery callback.

Cancellation persists intent and is monitored by the Runner. Queued work is cancelled without executing, completed journal results remain authoritative, and workflows receive the existing cancellation token. The custom-step script engine remains synchronous and cannot forcibly stop running script work; cancellation is not a claim that side effects stopped. A stopped Runner with accepted, unfinished work reports uncertainty when redelivered rather than repeating it. Workflow/custom-step bodies remain serial. Slice 2 permits concurrent Action management deliveries so artifact reads do not wait for an executing action; the existing four-action execution gate and tenant cgroup/storage limits remain in place.

Removed in this slice: `RunWorkflow`/`RunCustomStep` API constants, all four SignalR invocations, both Runner hub handlers, their hub executor dependency, and the unused API SignalR custom-step response DTO. No compatibility fallback is retained. App actions, files, managed artifacts, Studio notifications, supported tunnels and connection/drain coordination are deliberately retained for their listed slices. Apply the schema migration and release matching API/Runner versions after review; old Runner versions cannot consume the replacement execution path. Slice 1 release is recorded below; its live execution checks remain deferred.

#### Slice 1 deployed — 2026-10-01

PR #334 and the TLS rollout fix #335 are merged. API revision `b0ec235a78cad240f4e7481e2a404bad4aa8667a` is live. Normal Runner build #4916 published `rzvdaniel/automaton-runner@sha256:3acb4b0e2a4f50f44fc7295952bf3cd3843cad23498c87bdc6ac09377d74ed89`; the Infrastructure and Hosted Runners both run that digest. The previous protected pin and configuration backups were retained. Migration `20260930213225_AddRunnerExecutionOperations` is applied. Normal cutover/registration and production health checks passed.

The private TLS listener passed chain/hostname validation and restricted authenticated broker access from the Runner network. Installed .NET execution consumers connect through that listener; each execution queue retained one consumer with zero ready/unacknowledged deliveries. RabbitMQ sees the internal plaintext leg after Traefik TLS termination; this is not evidence of plaintext Runner connections. A pre-existing host-record private-address error initially blocked Hosted Runner reprovisioning; correcting the field through the supported Admin API to the verified private interface allowed reprovisioning. Existing app containers remained running.

**Deployed; live execution checks deferred to final RM acceptance.** No successful live workflow/custom-step execution or persisted-result acceptance is claimed: Studio's pages are placeholders and no test-account access token was obtained. The temporary sign-in helper was stopped. Deliberate interruption, cutover and package cross-host availability retain their separate status.

#### Slice 2 audit and implementation

Actual callers: `AppActionExecutionService` (manual/scheduled execution, cancel, modules, versions and artifact reads), `ScheduledActionExecutionHandler` (trusted module readiness), `TrustedInfrastructureActionDeploymentService` (list/prepare), `HostedRunnerStorageActionDeployment` (trusted measurement module readiness), and `AppDeploymentTargetRuntimeCleanupService` (target runtime/actions cleanup). The replaced Runner registrations were in `HubAppActionHandlerRegistrar`. `SynchronizeAppActionModules` had no API caller and its unused hub registration is removed; local deployment-time module synchronization remains unchanged.

These paths address stable Runner identity through the existing durable execution queue. RabbitMQ messages contain only operation/Runner IDs, including cancellation wakeups. Requests, module source archives, logs and results travel through authenticated HTTPS, using the existing protected operation material. There is no broker script/archive payload, new messaging framework or new storage service. Application access/execute/update checks, privileged target approval, trusted module allowlists, Runner-key binding, current manifest execution limits, timeout/result/log bounds and artifact expiry remain enforced by their existing owners. Fresh heartbeat replaces the action callers' SignalR connection requirement.

An action execution uses the same ID for its existing `AppModuleExecution` and durable Runner operation, queued transactionally. The authorized terminal HTTPS callback commits both results in one transaction; replay is already-applied. Expired dispatch cannot start late; interrupted/uncertain work is terminal rather than re-executed. Cancellation persists intent, publishes the existing identity-only command and reaches active processes through the existing HTTPS cancellation monitor; requesting cancellation is not proof that a process stopped. Module/version preparation and target cleanup also use journaled identities. Concurrent delivery keeps the existing four-action process gate and serial workflow/script gate, while allowing artifact reads alongside an action. File locks serialize duplicate operation processing. Private journal results are deleted after terminal API confirmation; minimal acceptance tombstones and lock inodes remain to prevent a stale in-flight assignment from executing again.

Removed: detached `AppActionExecutionDispatcher`; action execute/cancel, read-log/result, list-module/version, restore/delete-version, prepare/synchronize-module and target-cleanup hub constants/registrations; connection-ID plumbing in those callers; obsolete transport test doubles. `DeployAppPackage`, managed-artifact/file handlers, independent Studio notifications, supported tunnels and reconnect/drain gates remain for Slices 3–5. No transport fallback is retained. No Slice 2 release or live acceptance is claimed. Focused regressions and build results belong to this PR; final combined RM acceptance still includes Slice 1 execution and forced host-validation interruption. M1 remains complete.

### Infrastructure host-validation slice

Validation now persists its existing workflow operation and publishes an allowlisted, read-only `validate-runner-host` action through the transactional command outbox. The command targets the stable Infrastructure Runner/host identity; the authenticated HTTPS result applies the existing health/resource snapshot and provisioning/approval rules. Duplicate callbacks do not reapply snapshots. A superseded validation cannot overwrite a newer requested validation. The existing action journal replays completed results; interrupted acceptance becomes an explicit failed validation rather than rerunning uncertain work. Studio refreshes the persisted operation. No storage, compute or admission policy changes are included.

The API script executor/interface, `RunInfrastructureCommand` hub constant/Runner handler, and arbitrary script execution method are removed. Typed host-container Docker execution remains. This slice was released and its focused live validation passed on 2026-09-30, as recorded below. Keep reconnect/drain coordination for the remaining callers below. **RM remains open; M1 remains complete.**

#### Released host-validation acceptance — 2026-09-30

- PR #332 merged as `e5838d12e06f7aeca6fda4b2782b0a7c333ca534` after tests build #4902 passed on reviewed head `26ab5784`. Main tests build #4903 also passed.
- Normal API and Studio blue/green releases both run that merge revision on green. Previous blue images were retained; normal API Runner registration/drain and final `prod-verify.sh` checks passed. Worker and Hosted Runner image configuration were unchanged.
- Normal Runner pipeline #4904 passed on the same merge revision. Infrastructure Runner was updated through the installer to `rzvdaniel/automaton-runner@sha256:03a5a2b758e285a836eaee5b24dc04cb64ce30531aebd9bc0039bc0aafee3286`, preserving its identity and configuration. A protected configuration backup was taken first. The bundled validation script SHA-256 matches source: `f71b1666c2d074df9bd7b20f1ca6b8f8fab008b356da84c635449f9a9c0d34f3`.
- Studio **Validate**, followed by **Refresh**, completed operation `1e6a587d-6c43-44a2-be69-b6e758f65868` with persisted `succeeded`, structured result and durable stdout. Docker (`overlay2`), network, disk and API connectivity were healthy. Existing approval and enabled scheduling remained intact.
- An authenticated replay of that completed operation with an unavailable artifact returned HTTP 200, `already-applied`. A replay claiming a different Infrastructure Runner returned HTTP 403. The regression test additionally verifies no persisted snapshot, operation or log changes; the live replay check did not independently compare every database field.
- During release, existing host-container disposition requests encountered HTTP 429 and restarted the consumer automatically. Existing result delivery also honored a 60-second retry delay. The queue drained before update and returned to zero ready/unacknowledged messages with one consumer after live validation, without a further Runner restart. These were observed throttling events, not a deliberately injected host-validation interruption test.
- Existing Hosted Runner and app containers remained running. No disposable app was created or deleted for this read-only host-validation slice. No filesystem exhaustion, storage migration or resource-policy changes were performed.

This establishes normal live host validation and authorized completed-result replay. Deliberately forced interruption of this new action remains untested live; existing regression and earlier RM recovery evidence remain separate. It does not close the remaining RM management callers or broader recovery acceptance.

### Remaining SignalR inventory after this slice

| Flow | Current API caller / Runner handler | Next RM treatment |
| --- | --- | --- |
| Workflow execution | `RunnerFlowExecutionService`, `ScheduleService` / durable execution consumer | Deployed; live execution checks deferred to final RM acceptance. `RunWorkflow` removed. |
| Custom steps | `CustomStepRunnerExecutionService`, `AppsStepExecutionService` / durable execution consumer | Deployed; live execution checks deferred to final RM acceptance. `RunCustomStep` removed. |
| Infrastructure host validation | `InfrastructureWorkflowExecutionService` / bundled `validate-runner-host` action | Migrated and released; replaced `RunInfrastructureCommand` handler and API script transport removed. Normal live validation and completed-result replay passed on 2026-09-30. |
| App actions and action administration | `AppActionExecutionService`, `AppActionRunnerGateway`, `TrustedInfrastructureActionDeploymentService`, target cleanup / durable execution consumer | Migrated in Slice 2; review/release pending. Replaced action hub handlers and detached dispatcher removed. Generic `DeployAppPackage` remains for Slice 4. |
| Managed application artifacts | `ConnectedFlowArtifactService` / `SynchronizeApplicationManagedArtifacts` | Move synchronization to the durable artifact/command path. |
| App, media and generic Runner deployment transfers | `AppFileUploadService`, `AppFileDownloadService`, `AppMediaStorageService`, and the generic Runner fallback in `DeploymentPackageRunnerService` / file chunks, metadata, and `DeployAppPackage` | Replace SignalR chunk transfer with authenticated HTTPS and persisted operation results. This is distinct from the Hosted Runner RabbitMQ/HTTPS deployment path already released. |
| Runtime tunnels | `RuntimeTunnelRequestSender`, `RuntimeFileProxyService` / `TunnelRequest` and `TunnelResponse` | Keep supported BYO tunnel behavior if still required; do not use it as a management command transport. |
| Connection and cutover lifecycle | API Runner registration/presence plus `HubControlHandlerRegistrar` / `Ping`, welcome and reconnect-when-idle coordination | Keep reconnect/drain until the replaceable management callers above have moved; remove API-slot registration gating only after that. |

This inventory is based on current API hub call sites and Runner hub handlers at the branch point. Studio live updates remain an independent SignalR surface outside the Runner management migration.

In the merged PR #326 implementation, the storage telemetry API resolves the Runner from its API-key identity, validates its purpose and Hosted Runner/Runner Host association, then submits the report through the existing telemetry service. The Runner posts over HTTPS with its existing API key (HTTP is allowed only for loopback development). This removes storage-measurement callbacks from `AutomatonHub`; production rollout was not verified in the 2026-09-29 slot-switch run. It does not complete RM or change storage accounting/enforcement.

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

Inventory every Runner SignalR caller before removal. This slice removes the host-container execution hub method, API transport, Runner hub handler and associated transport test. Keep connection and drain behavior while the remaining workflows/actions and management/file-transfer callers still depend on SignalR. Later, delete the replaced management hubs/clients, file-transfer paths, connection registries, cutover acknowledgements, reconnect/drain coordination, settings and obsolete tests. Remove the requirement for all Runners to register on a new API slot before API traffic can switch.

Retain independent supported features, such as Studio live updates or BYO tunnel functionality, if they still require SignalR. Name those remaining uses in the final inventory. No management fallback to the retired transport; update runbooks and Admin documentation to match the implemented behavior.

**First vertical-path acceptance:** a CLI deployment command survives Runner retry; the Runner retrieves its package after an API slot switch using authenticated HTTPS; the operation reaches a persisted terminal state without a SignalR completion callback. Treat the existing shared package volume as usable for the tested same-host slot arrangement only. Availability when the two slots are on different API hosts remains unestablished; do not add a storage system for this first path.

Automated regression coverage checks that an active API service instance can serve a package staged before a simulated slot switch, and that a Runner retry after a persisted successful result does not apply the runtime again. These unit tests cover shared-path and journal behavior.

On 2026-09-29, two CLI deployments were used to control queue ordering on the same Hosted Runner. The first app used a deliberate startup delay and remained unacknowledged in the Runner's RabbitMQ consumer. A second app deployment was accepted and queued behind it (`1` ready, `1` unacknowledged, `1` consumer). The standard API redeploy/cutover changed traffic from green to blue while the first command was still processing; the previous API drained and both expected Runners registered on the new color. After the first deployment completed, the queued second command was consumed. The active blue API log recorded HTTP `200` for `GET /api/hosted-runner-commands/operations/ed74d46b-2aea-44e4-ae85-b1c0e5df7dd4/package`, followed by HTTP `200` for its result POST. The second CLI invocation reported `packageDeploy` pass and “Deployment package applied,” its Live app endpoint returned the expected response, and the command queue returned to zero ready/unacknowledged messages. `prod-verify.sh` passed after the switch. This establishes package retrieval and result submission through the newly active API slot for this same-host slot pair, without a SignalR completion callback.

An earlier attempt in the same session cut over before the Runner was ready and ended with a `Runner is not connected` warning; it was not counted as a pass. A separate successful deployment also completed across the route switch while its app was starting. No cross-host package test, forced Runner retry, or lost-callback recovery test was performed. The deployed API revision was `bb5c5a38` on both colors; this run did not deploy a new API or Runner image.

M1's separate disposable app checks are complete. RM's same-host package-after-switch and terminal-result check passed. The trusted host-container RabbitMQ/HTTPS slice and its result-delivery recovery follow-up are merged and released; the focused live deployment, failure-recovery and cleanup checks below passed. Runner retry/recovery, cross-host package availability, and the remaining SignalR caller migrations stay open.

### Infrastructure result-delivery recovery follow-up

The focused follow-up preserves the original delivery exception and lets the owning consumer scope tear down the channel after the callback returns. Infrastructure action and host-container result POSTs retry HTTP 408/429/5xx, transport failures and request timeouts while retaining the already computed result. `Retry-After` delta/date values control retry delay; cancellation interrupts waiting. Permanent HTTP rejection is not retried by the delivery component.

Fast unit tests force 429/503, transport failure, timeout and cancellation. Opt-in broker tests use a disposable RabbitMQ instance on existing server hardware through an SSH tunnel, with isolated queues and no database bootstrap. They verify that transient result delivery does not repeat host execution, the owning loop can replace a failed consumer, exactly one consumer remains, and no delivery is left queued after acknowledgement/teardown. These are broker/HTTP-response simulation tests, not a live Automaton API failure injection.

PR #329 records the earlier partial live acceptance, including manual restarts. PR #330 merged as `aca847a6407f8bbe366638a89b04002eb81445d1`; the focused live recovery checks below passed after release. No additional SignalR caller migration is included. RM remains open for its remaining callers and broader acceptance.

### PR #330 release and live automatic recovery (2026-09-30)

**Release:** normal Runner pipeline #4898 succeeded from merge `aca847a6`, including Docker CLI and bundled-action smoke checks. Its immutable image is `rzvdaniel/automaton-runner@sha256:19925026771ad5c6e75e6bd01a3ee3ff45af7ae7a62247ff5ea0b990b1622360`. The canonical installer updated only the Infrastructure Runner, which owns the changed result-delivery code, preserving Runner ID `37a08d13-79d0-45c4-a0b8-e1de32b8ceef` and installation state. API and Hosted Runner images remain at the previously released `5e95f583` contract/artifact; neither contains a changed execution path in #330. Worker, Studio, Website, Docker backend, scheduling and protected image-pin configuration were unchanged.

The Infrastructure Runner container was `f3aefe3c5d3db97f66ae2b1a45db2976ed5ade5e8fb1e82e4f8c1154502d2f65`, started at `2026-09-30T16:38:00.660154522Z`. Its ID/start time stayed unchanged throughout the fault checks and cleanup. There was no manual restart after release. The pre-release queue had one ready command and no consumer; the new consumer reconciled that retained delivery without purging it.

The retained app `RM328 Host Container Acceptance 20260930` (`33a731e1-7130-44fa-84d7-71f9f58bf453`) was used for two CLI deployments with a 90-second build. All times below are UTC.

| Check | Forced failure and observation | Terminal evidence |
| --- | --- | --- |
| HTTP 429 | Authenticated harmless disposition reads exhausted the existing rate limiter. Three bounded waves returned 913, 1100 and 912 HTTP 429 responses respectively, with `Retry-After: 60`. The live Runner logged 429 result-delivery retries at 16:40:20.914, 16:41:20.955 and 16:42:20.992, each with a 60-second delay; its consumer remained present. | CLI reported `success=true`, `runtime.success=true`, `state=ready`, and `runtimeRunning=true`. Docker recorded exactly one build creation (`423628326cb64e0ea03f85f0dd5ca0a2` attempt; container `660315a4b42a822f61021fc1a3308a4671a24119a14ee9a00e7bb5943f418111`) and one promoted app container (`bc7c666e159b4fc3bb32d10c6037855e`). |
| API connection failure | A temporary OUTPUT rule matched only the Infrastructure Runner cgroup and its API destination on HTTPS, from 16:44:56 to 16:45:16. The API and other Runners were not stopped. The live Runner logged the original `HttpRequestException` and inner `SocketException (111): Connection refused` while retrying its existing result. The trap removed the rule automatically. | CLI reported the same ready/success outcome. Docker recorded exactly one build creation (`09c2187e683c44c4ab654c602903c940` attempt; container `065e693c166aa6085a93834ad0407dd71045d747c617cfd6e1beb843ba797b97`) and one promoted app container (`60c8d76076594bb9a4a873cdfb5aa6d6`). The CLI's sole warning was the unchanged-source restore-point skip. |

After recovery, RabbitMQ reported `0` ready messages, `0` unacknowledged messages and exactly `1` consumer for `automaton.infrastructure.41d961eca33147fbaa86492bbdde5a7d`. Completed build containers were removed after result delivery. Docker creation events establish that neither fault repeated the build or app creation. This is live HTTP 429 and transport-failure evidence; HTTP 503 was covered by the focused tests, not injected into the live API.

**Cleanup:** Studio's supported Delete App flow returned HTTP 204 at 16:47:41 and navigated back to the app list. No running/stopped containers for this app remained. A file search across its workspace paths returned zero files. Empty parent directories remain under `workspace/apps`, `workspace/data` and `workspace/runner/deployments`; the shared account volume and the other running apps were retained. Final queue verification again showed `0 / 0 / 1`, the test firewall rule was absent, and `prod-verify.sh` passed.

The earlier result-delivery recovery finding is closed for these focused live scenarios. This does not close RM: remaining SignalR workflows/actions, file/artifact transfers, connection/drain dependencies, cross-host package availability and broader recovery scenarios retain their documented scope.
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
