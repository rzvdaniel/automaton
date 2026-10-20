# Automaton self-hosted roadmap

Updated: 21 October 2026. Status: M1/M2 implementation and the M3 installation/telemetry slices are merged. Next: demonstrate the single-server CLI deployment flow, then complete M4 legacy cleanup. Broader acceptance remains deferred.

## Product decision

Automaton is a self-hosted workspace where individuals and small teams deploy, share and run web apps and automations on machines they own or rent. Customers choose and pay for their infrastructure. Automaton supplies deployment, access control, routing, jobs and operational visibility with predictable infrastructure costs.

Refactor the existing product aggressively. Keep the working API, Studio, Worker, identity, app model, messaging, routing and Runner management mechanisms. Replace the duplicated execution and managed-hosting assumptions rather than rewriting those capabilities.

The target is **one management Runner per machine**, responsible for both machine management and application orchestration. User applications, builds and Actions run in separate containers. A shared Runner is a trusted management service, not a shared process in which users execute arbitrary code.

This is the only active roadmap. It supersedes the staged master plan, archived backlog and Node N0–N4 briefs. Their history remains in Git; their unfinished tasks are not implicit requirements of this plan. Do not introduce a separate Node executable or shared Runner/Node framework.

The current refactoring deliverable is a working single-server install/deploy flow **and removal of the superseded execution and hosting paths**, targeting fresh installations. Automatic migration of legacy Hosted Runner/account-storage installations, compatibility adapters, and preservation of their app IDs or URLs are not required. Finish the new flow, then remove the obsolete paths. Existing installations and their data are not changed by this source cleanup; any retirement, export or manual move needs a separate explicit decision. Optional operations and feature expansion remain deferred until after cleanup.

## Starting point and baseline

Existing Automaton already deploys and serves apps, runs Actions, routes traffic and groups account containers with resource controls. Infrastructure and Hosted Runners use the same executable, but differ in permissions, assignment, provisioning, storage and execution paths. Combining their names or registrations would leave that duplication and its security implications intact.

The source baseline is `57941d99` plus the registration-blocking and existing-user GitHub login/account-creation fixes from `649761f6`, reconciled with current main by PR #391. Its reviewed head was `489a4004d5f79a72f78e5982634a7cedb3fe25e3`; it merged as `bb12bf68f36b1b271f4ebfe6986add1ad190e125`. It removed the abandoned Node implementation and retained the roadmap, repository guidance, approved Symphony cleanup and shared DbContext cleanup. It added no Runner behavior. The [milestone 1 implementation guide](../implementation/self-hosted-milestone-1.md) is based on that merged source.

The source model and EF snapshot returned to the pre-Node shape, but source changes do not reverse applied SQL. The removed migrations were `20261012183817_AddNodeEnrollmentAuthentication` and `20261013095351_AddNodeActivationPersistence`. Fresh installation does not require converting an old database. Reusing an existing database would require a separate review of its `__EFMigrationsHistory` and an explicit reuse/reset/compatibility decision; this roadmap does not provide an automatic upgrade path or authorize database changes.

## Architecture to build toward

| Component | Responsibility |
| --- | --- |
| Studio, CLI and AI/MCP clients | Use the same API to manage apps, deployments, access and jobs. |
| API and MySQL | Own identities, permissions, machine assignment, desired deployment state and durable operation results. |
| Worker and RabbitMQ/CAP | Coordinate background work and schedules using the existing durable operation path. |
| Runner on each customer machine | Authenticate as that machine; reconcile its assigned workloads; manage containers, volumes, routes, logs and allowed maintenance operations. |
| App and job containers | Execute user code without Runner credentials, the Docker socket or unrestricted host access. |
| Traefik and optional static server | Route intended public/authenticated app traffic; serve published static output without exposing source, secrets or private data. |

The control plane and workloads may share a machine for a small installation or use separate machines. Protect control-plane capacity when they share. An organization can add machines and explicitly place workloads; Automaton does not promise automatic capacity provisioning or transparent movement of host-local data.

### Reuse and simplify

Start at these existing seams, confirming them against the agreed baseline before editing:

| Existing seam | Refactoring intent |
| --- | --- |
| [Runner service composition](../../src/Automaton.Runner/RunnerHostBuilder.cs) | Keep the executable; consolidate management and workload orchestration behind clear feature boundaries. |
| [App Runner authorization](../../src/Automaton.Api/Services/AppRunnerAuthorizationPolicy.cs) | Replace the current trusted-infrastructure-App restriction with explicit authorized workload operations. Do not grant ordinary apps infrastructure permissions. |
| [Typed container execution](../../src/Automaton.Runner/Services/HostContainerExecutionService.cs) | Reuse bounded container operations; replace mandatory Hosted Runner resource mappings with authorized machine/workspace/app mappings. |
| [Hosted app capacity](../../src/Automaton.Api/Services/HostedRunnerAppCapacityService.cs) | Move placement and capacity ownership to machines and workloads instead of manufacturing a Hosted Runner per user. |
| [Runner management transport](../architecture/runner-management-transports.md) | Preserve durable operation IDs, protected assignments, journal/retry behavior, logs and result delivery. Adapt identity bindings where necessary. |

Within the new path, retain stable app, deployment and execution identities across normal operations. This does not require preserving legacy-installation IDs or URLs. Container names and machine names are not ownership evidence. Keep source editing separate from disposable runtime output; a running container must not become the only copy of an app's source.

### Deployment contract

Support Linux container workloads: HTTP services, background workers and finite jobs/Actions. Accept an OCI image, a reproducible Dockerfile/source build, or a documented subset of Compose for related services. Translate existing app contracts into this model rather than maintaining two execution engines indefinitely.

The M2 Compose subset is implemented in PR #398: internal service networking, one private externally routed HTTP service, app-scoped named data volumes and existing per-container resource limits. Unsupported fields are rejected before resource creation. Compose builds and secure secret delivery remain deferred; Compose environment values are non-sensitive literals only. Generic deployment must not accept arbitrary host mounts, privileged containers, host networking or a Docker socket. Builds execute untrusted code and require a separate bounded container boundary.

Shared static publishing is complete in merged PR #400: one Runner-managed serving container per machine, with site-specific output directories mounted read-only. API authorization and runtime authentication govern each hostname; the Runner publishes only declared static output. A failed update leaves the prior site version serving. Source, credentials, logs and private data are excluded.

Machine Management Actions and Python dependency preparation use bounded containers, retaining their existing execution identities, schedules, results, cancellation and logs. Raw workflows and management-process custom steps remain disabled on this profile. Preserve these boundaries during cleanup; implementing another workflow execution path is not required for the current refactoring.

### Workspaces and machines

Keep personal spaces, shared projects and an organization app library. Distinguish who owns an app, who may use/develop/administer it, and where it runs. Sharing access to an existing app and copying its source are different operations; copies must not silently include production secrets or private data.

A customer can dedicate a machine to a person, team or environment, or share it within the organization. A VM per user is optional customer infrastructure, not a requirement or something Automaton automatically provisions. Container separation on a shared machine is not equivalent to a VM boundary for mutually untrusted tenants.

### Resources and storage

Use a normal Ubuntu machine as the initial supported installation target; ext4 is the tested storage baseline, not a runtime requirement. Working Docker storage on XFS or another supported filesystem remains valid. Basic deployment must not require XFS project quotas, a Hetzner volume or any provider-specific storage attachment. Persistent app data uses explicit, owned directories/volumes on configured storage roots and survives normal redeployment. Converting legacy deployments is outside the fresh-install scope.

Reuse configured CPU/memory/process limits, bounded build/job concurrency, log rotation and minimum-free-disk admission. Basic workspace-filesystem capacity and measurement status are sufficient for the current refactoring. Additional image/layer/volume/log/build usage breakdowns and automated retention/cleanup are deferred until after legacy cleanup. Any cleanup must delete only identified disposable resources and preserve owned persistent data.

Report disk monitoring and admission checks honestly: these do not enforce a hard per-user disk limit on ext4. Optional hard storage quotas need an actually supported backend and explicit capability reporting. Customers requiring a hard whole-workload boundary can supply a dedicated VM; do not build a hypervisor manager for this release.

### Authorization, recovery and operations

Bind Runner credentials and accepted operations to a machine and authorized app/workspace assignment. Check permission for deployment, files, logs, exec, results and cancellation; UI visibility alone is insufficient. Reject stale/revoked credentials and prevent concurrent management ownership after a cloned installation or restart. Reuse existing mechanisms where they meet those requirements; specify missing transitions in the implementation PR.

Keep authoritative state durable and safe with multiple API/Worker replicas. Duplicate delivery must return the existing result or safely resume a known operation. An Action with an uncertain external effect must not be blindly rerun. Reuse the existing journal/result protocol instead of introducing another queue or operation store. The existing cross-host artifact-availability limitation must be resolved or explicitly constrained in supported deployment topology before claiming replica/host failover.

After a database restore or a machine identity conflict, stop new effects until machine ownership and outstanding operations are reconciled. Existing app containers may continue serving during a control-plane outage; recovery must not silently adopt foreign volumes or start duplicate jobs. Normal app replacement must preserve its persistent data and recover from a failed update. Automatic legacy migration, a backup product and the abandoned Node authority system are not prerequisites for this refactoring.

An optional system-app catalog for monitoring, backup and selected maintenance is later work. Do not build it before the core flow and legacy cleanup. Any future host-privileged maintenance needs explicit administrator authorization and a narrow trusted capability.

## Delivery milestones

M1/M2 implementation is merged; their broader integration acceptance remains deferred. M3 now closes with the fresh single-server demonstration below, followed directly by M4 legacy removal without a migration product. Source inventory and isolated cleanup work can begin while that demonstration is assembled. Deliver cohesive working changes; use several PRs only where reviewability or a necessary dependency warrants it. Do not create another tree of speculative briefs or implement supporting abstractions without a current consumer.

### 1. One machine Runner can deploy apps and run isolated jobs

The implementation PRs are merged on the reconciled source baseline from PR #391 and retain the existing control plane and durable Runner operation mechanisms. Reusing or changing an existing installation/database is separate from fresh-install development and requires an explicit operational decision. See the [milestone 1 implementation guide](../implementation/self-hosted-milestone-1.md) for source details and acceptance cases to revisit before relying on the assembled flow for other users or replacing the active Runner.

- Establish machine/workspace/app assignment and capacity ownership without allocating a per-user Hosted Runner. Preserve existing API authorization and durable operation semantics.
- Implement a complete API-to-Runner web deployment on ext4: build or pull, create/start, health/readiness, routing, logs, stop/update and explicit persistent data.
- Run a finite Action in an isolated job container with results, cancellation, bounded resources and cleanup. Keep user code out of the management service.
- Update the affected Studio/CLI/API surfaces and administrator documentation in the same changes. The M1 implementation retained legacy execution alongside the new path; M4 now removes that code without adding migration support. Do not broaden infrastructure permissions as a shortcut.

Acceptance: on a disposable Linux machine, two users deploy apps and run jobs through one management Runner. Cross-user files, secrets, logs and operations are denied. There is no placeholder Hosted Runner record or per-user management container required for new workloads. Restart/lost-response checks demonstrate safe result recovery and no automatic replay of uncertain Actions. Source/artifacts and persistent data survive the applicable restart/update scenarios.

M1 implementation PRs are merged. Combined integration acceptance is deferred because a suitable isolated environment is unavailable. Real Action execution, two-user isolation, cancellation and combined restart/replay behavior remain unverified.

### 2. Make personal and shared apps useful with low overhead

Depends on milestone 1's execution and permission boundary.

- PR #396 completed Action authorization for explicitly assigned organization apps. PR #397 completed the organization app use flow—assignment, My Apps listing, runtime launch, Action access and revocation—while keeping app use separate from source, secrets and machine administration.
- Capacity safeguards in PR #401 reuse configured per-container CPU, memory and PID limits for builds, app/Compose containers, Action jobs and dependency preparation. Deployment dispatch and the Runner Action queue already bound concurrent deployments and jobs. Machine Management work also checks free space on the configured workspace and Runner storage filesystems against `MachineContainerResources:MinimumFreeDiskMb` (default 1024 MB) before deployment preparation, container creation, Action work and static publication; deployment refusal cleans staging and preserves the current runtime. This is admission only, not a hard ext4 quota. Aggregate reservations across concurrent API replicas and per-app/per-user disk accounting are outside scope; subscription quotas are also outside scope.
- Compose builds and secure secret delivery remain deferred and are not implied by the supported Compose subset.
- PR #403 completed the source-copy/archive safeguards and deployment failure details. Source copies and marketplace forks copy portable source only. They exclude configured secret/private/runtime paths and filesystem links; environment values in build, runtime and Compose contracts are cleared from fork previews and copies because the contract has no secret/non-secret distinction. Failed deployment summaries expose the latest stored failure reason, including configured disk-reserve refusal.

Implementation slices are complete, including organization app use (#396–#397), the supported Compose subset (#398), shared static publishing (#400), and basic capacity safeguards (#401). Focused copy/fork, authorization, deployment, and limit checks exist; the integrated small-team acceptance across two static sites, Compose, Actions, authorization and duplicate delivery has not been completed. Compose builds and secure secret delivery remain deferred.

### 3. Make installation and routine operation practical

Use the existing API, Studio, Worker and durable operation path. The immediate goal is one customer-owned Ubuntu server with one Machine Management Runner; a new control-plane installer or second-machine acceptance is not a prerequisite for this demonstration.

Implemented: PR #404 checks Ubuntu and Docker availability without requiring ext4 specifically. PR #405 adds periodic workspace-filesystem total/free capacity, measurement age/status and errors in Studio. Online state, version, heartbeat and assigned deployment targets are already available. These implementation slices do not establish live installation acceptance.

Remaining sequence:

1. On an isolated Ubuntu/Docker host, run the existing Studio-generated installation command and confirm the management Runner enrolls and connects.
2. Follow the actual CLI flow to create an app, assign its workspace/deployment target to that Runner, deploy it and open its routed URL. New workloads must not need a per-user Hosted Runner.
3. Redeploy the app and verify its explicit persistent data survives. A failed replacement must preserve the prior healthy app. Fix only concrete installation, assignment, CLI, readiness, routing or persistence blockers encountered.
4. Keep the working prerequisites and commands in the existing [self-hosted operations guide](../prod/self-hosted-runners.md) and relevant CLI documentation; record the tested revision and result in the implementation PR.

This single-server flow is the immediate M3 completion target. It does not close the deferred M1/M2 multi-user, Action, cancellation or restart/replay evidence. Preserve the new path's authorization, isolation, resource limits, durable operation behavior and app-update recovery; legacy compatibility is not a reason to retain old execution branches.

Manual backup requirements remain documented. Machine Management installations have no supported update/rollback procedure; re-enrollment and legacy Infrastructure Runner commands are not upgrade instructions. Backup automation and Runner self-update are deferred.

### 4. Remove superseded execution and hosting paths

This is the next major implementation objective after the fresh single-server flow. Begin the source/consumer inventory and isolated refactoring without waiting for optional M3 features or the entire broad acceptance fixture. No automatic workload migration, old/new dual execution, legacy-ID/URL preservation or generic compatibility framework is required.

- Find callers and dependencies of per-user Hosted Runner provisioning, duplicate Infrastructure/Hosted role-specific execution and management, account/subscription compute allocation, mandatory XFS/project-quota storage, and provider-specific storage/provisioning assumptions. Include API, Studio, CLI, Worker, registration and queue paths.
- Move the new path's actual callers to the existing machine/workspace/app/target model and remove the obsolete branches, endpoints, UI, handlers and configuration. Remove their obsolete tests and documentation; keep focused coverage of the replacement flow. Do not preserve an old execution engine solely for legacy installations.
- Keep generic identity, organization, subscription and operation/journal behavior that the new path still uses. Preserve its app data across ordinary updates and prevent automatic replay of uncertain Actions. Removing migration support does not remove those normal runtime guarantees.
- Keep the schema/model required by fresh installations consistent. Add only the schema changes the new path needs; do not build old-data conversion machinery or claim an automatic upgrade of existing databases.
- Document the breaking installation boundary. Existing legacy installations may remain on their current release; retirement, export, manual redeployment, database reset/reuse and volume removal are separate operator decisions. This source cleanup must not access or delete their data.

Completion: a fresh machine installation, CLI deployment, routed app and redeployment with preserved data work through one management Runner, and the final source/registration/queue audit finds no active legacy execution consumer in the replacement implementation. Old-installation migration and preservation of old app identities/routes are not acceptance criteria. Production deployment, database changes and storage removal remain separately authorized operations.

### Deferred until after the core flow and legacy cleanup

These are later improvements, not prerequisites for M3's single-server target or M4 source cleanup:

- Second-machine acceptance and additional multi-machine operating features. Preserve existing enrollment/assignment behavior.
- Additional monitoring, per-category storage breakdowns, automated retention/cleanup and system-app catalog work.
- Runner self-update/rollback and bootstrap/update helpers.
- Backup automation, scheduling, destinations and restore tooling. Keep the documented manual backup guidance; it does not imply a legacy migration feature.
- Compose builds, secure Compose-secret delivery, and further Compose, marketplace and sharing enhancements.

The broader M1/M2 acceptance remains recorded in the [M1 guide](../implementation/self-hosted-milestone-1.md) and milestone summaries above. It is still needed before claiming those guarantees for broader use; it must not be expanded into an unrelated feature program or a gate on every isolated cleanup PR.

## Verification and working rules

Keep verification proportional to changed behavior. Run focused contract/unit checks for actual decision logic, Linux fixtures for filesystem/container behavior, and real-database checks when changing migrations, SQL predicates or concurrency. A build alone cannot establish those behaviors; unrelated broad suites need not run after every documentation edit.

For runtime changes, follow repository-required affected-project and full-solution builds. Run the end-to-end acceptance relevant to the milestone and review the final source revision. Record commands, results and unavailable checks in the implementation PR; do not duplicate lengthy logs in this roadmap. Review/QA may use separate agents when requested, without adding an orchestrator product.

Update the milestone status here only with its evidence and remaining limitations. Implementation may proceed and be tested in isolated fixtures without claiming production readiness. Production credentials, deployment and data changes remain separate operations.

## Deliberately outside this release

Public multi-tenant hosting, automatic cloud capacity purchasing, managed VM/microVM creation, Kubernetes, cross-region failover, distributed storage, a replacement message bus, a new workflow engine and a custom agent orchestrator are outside this roadmap. Optional provider integrations can be considered after the self-hosted path is useful and simple to operate.

**Next action:** demonstrate one management Runner installed on an Ubuntu/Docker server, CLI app creation/assignment/deployment, a working URL, and redeployment with preserved data. Fix only blockers to that flow, then proceed directly to M4 legacy deletion for the fresh-install target. Optional operations and feature expansion stay deferred until after cleanup. Automatic legacy migration and compatibility preservation are outside scope. Keep previously deferred acceptance and any existing-system deployment/data decisions separate from isolated implementation work.
