# Automaton self-hosted roadmap

Updated: 24 October 2026. Status: M3 and M4 are complete. The single-machine install/deploy/persistence flow is demonstrated, source cleanup/audit is merged through #416, and the demonstrated routing/CLI corrections are merged in #417 (`a442dda3`). M1/M2 implementation is merged; broader Action and multi-user acceptance remains deferred before team use.

## Product decision

Automaton is a self-hosted workspace where individuals and small teams deploy, share and run web apps and automations on machines they own or rent. Customers choose and pay for their infrastructure. Automaton supplies deployment, access control, routing, jobs and operational visibility with predictable infrastructure costs.

Refactor the existing product aggressively. Keep the working API, Studio, Worker, identity, app model, messaging, routing and Runner management mechanisms. Replace the duplicated execution and managed-hosting assumptions rather than rewriting those capabilities.

The target is **one management Runner per machine**, responsible for both machine management and application orchestration. User applications, builds and Actions run in separate containers. A shared Runner is a trusted management service, not a shared process in which users execute arbitrary code.

This is the only active roadmap. It supersedes the staged master plan, archived backlog and Node N0–N4 briefs. Their history remains in Git; their unfinished tasks are not implicit requirements of this plan. Do not introduce a separate Node executable or shared Runner/Node framework.

The core refactoring delivered a working single-server install/deploy flow **and removal of the superseded execution and hosting paths**, targeting fresh installations. Automatic migration of legacy Hosted Runner/account-storage installations, compatibility adapters, and preservation of their app IDs or URLs are not required. Existing installations and their data are not changed by this source cleanup; any retirement, export or manual move needs a separate explicit decision. Optional operations and feature expansion remain deferred.

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
| App assignment | The legacy subscription-spec app-count gate is removed; machine/workspace safeguards govern new workloads. |
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

M1/M2 implementation is merged; their broader integration acceptance remains deferred. M3/M4 are complete: the fresh single-server demonstration passed, its corrections are merged, and legacy removal and its final audit are merged. No legacy migration product is required. Further changes should fix concrete problems found through normal use, without another tree of speculative briefs or supporting abstractions without a current consumer.

### 1. One machine Runner can deploy apps and run isolated jobs

The implementation PRs are merged on the reconciled source baseline from PR #391 and retain the existing control plane and durable Runner operation mechanisms. Reusing or changing an existing installation/database is separate from fresh-install development and requires an explicit operational decision. See the [milestone 1 implementation guide](../implementation/self-hosted-milestone-1.md) for source details and acceptance cases to revisit before relying on the assembled flow for other users or replacing the active Runner.

- Establish machine/workspace/app assignment and capacity ownership without allocating a per-user Hosted Runner. Preserve existing API authorization and durable operation semantics.
- Implement a complete API-to-Runner web deployment on ext4: build or pull, create/start, health/readiness, routing, logs, stop/update and explicit persistent data.
- Run a finite Action in an isolated job container with results, cancellation, bounded resources and cleanup. Keep user code out of the management service.
- Update the affected Studio/CLI/API surfaces and administrator documentation in the same changes. The M1 implementation retained legacy execution alongside the new path; M4 now removes that code without adding migration support. Do not broaden infrastructure permissions as a shortcut.

Acceptance: on a disposable Linux machine, two users deploy apps and run jobs through one management Runner. Cross-user files, secrets, logs and operations are denied. There is no placeholder Hosted Runner record or per-user management container required for new workloads. Restart/lost-response checks demonstrate safe result recovery and no automatic replay of uncertain Actions. Source/artifacts and persistent data survive the applicable restart/update scenarios.

M1 implementation PRs are merged. Combined integration acceptance remains deferred; the single-user M3 demonstration does not verify real Action execution, two-user isolation, cancellation or combined restart/replay behavior.

### 2. Make personal and shared apps useful with low overhead

Depends on milestone 1's execution and permission boundary.

- PR #396 completed Action authorization for explicitly assigned organization apps. PR #397 completed the organization app use flow—assignment, My Apps listing, runtime launch, Action access and revocation—while keeping app use separate from source, secrets and machine administration.
- Capacity safeguards in PR #401 reuse configured per-container CPU, memory and PID limits for builds, app/Compose containers, Action jobs and dependency preparation. Deployment dispatch and the Runner Action queue already bound concurrent deployments and jobs. Machine Management work also checks free space on the configured workspace and Runner storage filesystems against `MachineContainerResources:MinimumFreeDiskMb` (default 1024 MB) before deployment preparation, container creation, Action work and static publication; deployment refusal cleans staging and preserves the current runtime. This is admission only, not a hard ext4 quota. Aggregate reservations across concurrent API replicas and per-app/per-user disk accounting are outside scope; subscription quotas are also outside scope.
- Compose builds and secure secret delivery remain deferred and are not implied by the supported Compose subset.
- PR #403 completed the source-copy/archive safeguards and deployment failure details. Source copies and marketplace forks copy portable source only. They exclude configured secret/private/runtime paths and filesystem links; environment values in build, runtime and Compose contracts are cleared from fork previews and copies because the contract has no secret/non-secret distinction. Failed deployment summaries expose the latest stored failure reason, including configured disk-reserve refusal.

Implementation slices are complete, including organization app use (#396–#397), the supported Compose subset (#398), shared static publishing (#400), and basic capacity safeguards (#401). Focused copy/fork, authorization, deployment, and limit checks exist; the integrated small-team acceptance across two static sites, Compose, Actions, authorization and duplicate delivery has not been completed. Compose builds and secure secret delivery remain deferred.

### 3. Make installation and routine operation practical

**Complete.** The existing API, Studio, Worker and durable operation path support the demonstrated single-machine flow. A new control-plane installer and second-machine acceptance are not prerequisites for this completed slice.

PR #404 checks Ubuntu and Docker availability without requiring ext4 specifically. PR #405 adds periodic workspace-filesystem total/free capacity, measurement age/status and errors in Studio. Online state, version, heartbeat and assigned deployment targets are already available. The demonstration below supplies the installation acceptance evidence.

Demonstrated on 23 October 2026 using a separate development API, Studio, Worker, MySQL, RabbitMQ and Management Runner on one Ubuntu test host. The actual Docker/workspace filesystem was XFS; this is not new ext4-specific evidence. API/Studio/Worker and the CI-published Runner used `38d994c2`; the CLI failure-reporting amendment was rebuilt and rerun against that stack.

- The generated installation command enrolled one Management Runner. The workstation CLI created an app before assignment, assigned it, deployed an image-based HTTP app and opened its published URL in a browser. The fresh development database contained no Hosted Runner records.
- Redeployment changed version 1 to version 2 while preserving a marker created only under `AUTOMATON_APP_DATA_DIR`. An intentionally failing replacement retained version 2 and that marker; the corrected CLI returned failure/exit 1. A final healthy redeployment succeeded. Container inspection confirmed CPU/memory/PID limits and a read-only root filesystem.
- The run found and corrected two concrete blockers: the development edge lacked the default `api-live-edge` service name, and the CLI treated a failed runtime deployment as successful with warnings. The demonstrated corrections at `c1125734` merged in [PR #417](https://github.com/rzvdaniel/automaton/pull/417) as `a442dda3`. See the [operations guide](../prod/self-hosted-runners.md) for the working flow and prerequisites.

The previous workstation-to-workload networking blocker was avoided by placing the isolated development control plane and Runner on the same test host. Existing databases and app volumes were preserved; only the separate development database was initialized/migrated. This does not close the deferred M1/M2 multi-user, Action, cancellation or restart/replay evidence. Second-machine acceptance remains deferred.

Production own-use followed on 24 October using matching API/Studio/Worker/CLI/Runner source `7ce95744` after database and legacy-workload inspection. A fresh production Management Runner deployed Hello World and served its HTTPS URL; redeploying version 2 preserved its data marker. The additive telemetry migration applied, public registration remained disabled, and legacy data/rollback artifacts were retained. See the [production operations evidence](../prod/production-docker-cheat-sheet.md#production-own-use-release-24-october-2026). This does not close the broader M1/M2 acceptance.

Manual backup requirements remain documented. Machine Management installations have no supported update/rollback procedure; re-enrollment and legacy Infrastructure Runner commands are not upgrade instructions. Backup automation and Runner self-update are deferred.

### 4. Remove superseded execution and hosting paths

**Complete.** Source cleanup and the final audit merged through #416 (`38d994c2`); the fresh single-machine flow is demonstrated and its routing/CLI corrections merged in #417. No automatic workload migration, old/new dual execution, legacy-ID/URL preservation or generic compatibility framework is required.

PRs #407–#410 removed automatic Hosted Runner fallback, per-user provisioning and its Admin/Worker controls, the subscription-spec app-count assignment check, and the separate deployment queue/callback transport. App creation remains independent of machine assignment; deployment uses the assigned Runner's durable typed operation and existing result journal location.

Hosted Runner storage suspension/resume is now retired: automatic enforcement, Admin mutation controls, trusted Actions and resume-on-registration handling are removed. The Worker disables the retired system enforcement schedule; already-published work has no executable handler. Stored workload, enforcement and audit records remain historical; retirement does not resume suspended workloads. No existing database was accessed or migrated during this work. Machine Management filesystem telemetry, container limits and free-disk admission remain in place.

Reconnect-driven automatic app assignment and Hosted enrollment-history target rebinding are removed. App placement uses explicit assignment; reconnect records authorized presence without changing app placement. PR #412 is validated locally: reconnect/assignment tests (16), Core profile tests (7), affected-project builds and the full nonincremental solution build pass. Broader M1/M2 acceptance remains separate.

Hosted Runner self-reporting, host-side verification, automatic verification dispatch and account-storage locality/adoption are removed. The API and Runner reject retired measurement work; the Worker disables its old Hosted reconciliation schedule and no longer touches retained Hosted measurements. Historical database/allocation/audit records remain; no migration or data operation is included. Machine Management workspace-filesystem telemetry and workload safeguards are unchanged. Focused local regressions pass (117 tests), along with Studio checks and the nonincremental solution build; broader M1/M2 acceptance remains deferred.

Infrastructure host storage reporting/reconciliation and the mandatory separate-XFS image-store validator are removed. Storage schedules are retired without deleting historical records. Typed container admission now requires a Machine Management Runner; old Hosted-to-Infrastructure admission and account reserve policy are removed. Machine telemetry and server-derived workload limits/free-disk admission remain intact. Local validation passes: 47 focused tests, affected-project/full nonincremental builds and Studio type-check/build. That source evidence is supplemented by the single-machine M3 demonstration above.

Hosted restart/delete controls and callbacks, registration/reconnect/restart mutation, background lifecycle reconciliation, and the old Infrastructure host-container result/disposition path are removed. Addressed retired queue deliveries terminally acknowledge without execution. Runner typed execution accepts only a Machine Management scope and uses its installation-owned workspace volume and per-container limits; account volume names and Hosted cgroup parents are removed. Historical model/audit rows remain, with no migration or deletion. Generic enrollment/authentication, host validation and durable machine app/Action operations remain. Local focused regressions and required builds pass; the M3 section records the subsequent single-machine demonstration.

The final source/registration/queue audit removed Hosted provisioning readiness/retry states, direct Hosted endpoint route generation, the Runner Hosted credential/enrollment fallback, the unregistered host-provisioning reconciler, and obsolete local setup provisioning controls/configuration. Retained Hosted assignments fail with explicit Management Runner guidance and are not adopted into tunnel routes. Remaining historical records, denial guards and broker revocation cleanup are intentional; generic enrollment, host validation and durable machine operations remain. The single-machine demonstration and merged corrections provide the remaining fresh-install evidence. No additional feature milestone is needed.

The completion criteria are covered: a fresh machine installation, CLI deployment, routed app and redeployment with preserved data work through one management Runner, and the final source/registration/queue audit found no active legacy execution consumer in the replacement implementation. The fresh-install schema and generic identity/organization/operation boundaries remain; this does not establish the deferred Action restart/replay acceptance. Existing legacy installations may remain on their current release. Retirement, export, manual redeployment, database reset/reuse and volume removal are separate operator decisions; source cleanup does not authorize accessing or deleting their data.

### Deferred work

These are later improvements, not prerequisites for M3's single-server target or M4 source cleanup:

- Second-machine acceptance and additional multi-machine operating features. Preserve existing enrollment/assignment behavior.
- Additional monitoring, per-category storage breakdowns and system-app catalog work. Basic host retention/admission is covered by the [operations policies](../prod/production-docker-cheat-sheet.md#disk-cleanup); applying those policies to existing hosts remains an operator step, not milestone acceptance.
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

**Next action:** continue using production for our own apps and fix concrete problems encountered through normal use. The production checkout is reconciled and #418's host retention timers are active on both servers; retained older containers keep their existing log settings until normal replacement. See the operations guide for release/maintenance evidence. Before broader team use, complete the outstanding real Action execution, two-user isolation, cancellation and combined restart/replay acceptance. Keep backup automation, Runner self-update and the other deferred features postponed. No new feature or preparation milestone is needed. Existing-installation retirement and further production/data changes remain separate operator decisions.
