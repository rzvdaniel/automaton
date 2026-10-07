# Automaton: a simpler platform for publishing web apps

Date: 7 October 2026.

Status: proposed replacement roadmap. No implementation, production migration, repository publication or resource purchase is authorized by this document.

This roadmap supersedes the earlier external VM migration and fresh-project proposals as the recommended next direction. It also replaces the future implementation sequence in the [previous platform completion roadmap](managed-platform-completion-plan.md); that document retains historical implementation and production acceptance evidence. Evolve the existing repository in its existing checkout. Ship one replacement architecture, with no supported legacy backend, protocol adapter, dual writes or old execution mode. Development milestones precede one coordinated production cutover.

## 1. Recommendation and product boundary

**Automaton should make it easy to put a web app in front of its intended audience, then keep it running on a machine the user chooses.** A promising audience is people who can describe or generate an app but do not want to learn deployment operations. Treat that audience and the business opportunity as a hypothesis to validate, not a settled product requirement. Offer the same product in three configurations:

- Automaton hosts the control plane and provisions a dedicated deployment VM for the customer.
- Automaton hosts the control plane; the customer connects a fresh VM under their ownership.
- A company installs the complete control plane on its premises and connects its own deployment VMs.

Use the same release artifacts, API, Runner and deployment contract in all three configurations. Commercial hosting adds provisioning and billing policy; it must not introduce another app execution implementation. Self-hosted installations must function without an Automaton cloud account, license server, hosted AI service or hosted control plane.

The product hypothesis to validate is: **install Automaton on a supported fresh server and turn it into a place where people can safely publish and operate web apps without learning routine DevOps.** The first release should cover app publishing with HTTPS, updates, health/logs, declared data and recovery. Keep external AI coding tools effective through the CLI and API. AI-generated code receives exactly the same runtime restrictions as any other application.

Treat “install Automaton” as a product experience, not a promise to safely take over arbitrary existing servers. The supported bootstrap must inspect prerequisites, show the intended changes, obtain owner authorization for privileged setup and resume safely after interruption. Start with a fresh, documented VM image. Machine administration should grow from observable health and tested backup/restore into narrowly scoped, auditable maintenance operations; do not begin with an unrestricted root agent, arbitrary shell, or automatic OS mutation. Existing apps and data must never be erased or reconfigured implicitly.

Stage 0 must choose the simplest initial installation shape based on user testing: (A) one self-hosted appliance where control plane and apps share one server, or (B) an installed control plane managing one or more separate hosting nodes, including customer-owned nodes. These shapes have different failure and security boundaries. A single server is easier to install and operate but couples control-plane availability and resources to app workloads; a separate control plane lets apps keep serving during control-plane outages and scales across machines, but adds networking and operational components. Do not implement both as separate product forks by default. The hosted Automaton SaaS may need a control plane distinct from customer app nodes even if the self-hosted starter experience is all-in-one.

Position the user outcome as `idea or generated app → useful web app → shareable link → reliable operation`. The first publishing experience should let an owner give an intended audience a working app link without asking that audience to install Automaton, Docker or source code. Owners need a clear choice between public apps and access-controlled apps, and a way to update or withdraw the shared app. Supporting the handoff and operation of apps is the hypothesis; building a no-code or AI coding environment is not a prerequisite.

AI builders already pitch app creation plus one-click link sharing, and Replit has documented an app directory where people run, react to and remix published apps. Therefore, “AI can build it,” “deploy it with one click,” or “share a URL” alone is not an Automaton differentiator. A possible wedge is carrying a generated app into a customer-chosen managed, BYO or on-premises runtime with a clear ownership, access, update and recovery story. This combination still requires user validation. [Lovable publishing](https://lovable.dev/build/mobile-app-builder), [Replit app sharing](https://replit.com/blog/apps).

The initial release promises dependable delivery and sharing for an owner's chosen audience, not public app discovery. A browsable app catalog, rankings, comments, reviews, ratings, creator following, payments, public remixing and native mobile app store submissions require separate product decisions for curation, abuse handling, malware/privacy review, intellectual property and support. Do not imply these capabilities from a public URL. Consider them only if Stage 0 validation shows that discovery or adoption is the unsolved customer problem.

Remove the Actions/workflow/scheduling product from the replacement release, including custom steps, arbitrary host scripting, execution UI and command transports used only by those features. Archive required historical records and user-authored source during migration. Do not keep executable compatibility machinery for them. Deployment reconciliation and internal housekeeping remain ordinary platform responsibilities.

The hosted AI authoring/agent runtime, visual workflow designer and management-app marketplace are outside this release. Preserve exportable user material; remove their runtime dependencies from the deployable platform core. A future authoring integration must use the public app API and produce the same deployable artifact.

This scope intentionally accepts a smaller product and some breaking changes. The migration inventory must expose those changes account by account before cutover.

### SaaS and on-premises operating model

Automaton SaaS is a first-class delivery configuration of this same open-source platform. Offer managed hosting and BYO hosting through the same hosted Studio, API, CLI and Runner protocol. A fully self-hosted installation uses the same versioned artifacts with local configuration and billing/provider purchase features disabled. Do not create separate hosted and on-premises product forks.

The operator reports the current deployment as API/Studio on one server and customer app containers on a second server. Preserve that separation of control-plane and application workloads. Consolidate the control-plane services on the first server initially, subject to measured capacity. Replace shared customer placement on the second server with provider-managed account VMs, allocated as customers provision managed hosting. Existing customer data on the second server follows the offline migration before that server is retired or reassigned; do not assume it is empty or can be repurposed during development.

The second server is not automatically a VM host in this plan. Running our own hypervisor fleet would add VM packing, physical-host recovery and another infrastructure lifecycle; defer it. Customer-owned VMs, including suitable VMs hosted on a company's own virtualization infrastructure, enroll through the same Runner contract without Automaton managing that hypervisor.

Define operational responsibility explicitly:

- **Managed SaaS:** Automaton operates the control plane and the provisioned account VM, including supported Runner/runtime updates, OS maintenance, DNS/certificates for platform domains, agreed backups and recovery procedures. App code and external app dependencies remain the customer's responsibility unless separately managed. OS maintenance starts with an operator runbook; automatic OS administration remains deferred.
- **SaaS with a customer VM:** Automaton operates the control plane and supplies the supported Runner/runtime software. The customer owns the VM, OS maintenance, network/DNS access and external dependencies. Enrollment authorizes the documented app operations, not unrestricted machine administration or deletion. State who operates configured backups and verifies restores before enabling stateful hosting.
- **Fully self-hosted:** the installation owner operates the control plane, VMs, registry/artifact storage, network, updates and backups. Automaton supplies the same release packages and tested installation/upgrade/recovery documentation. Operation does not depend on an Automaton cloud account or commercial billing service.

For each mode, the customer workflow is connect/provision a machine, publish an app, attach a domain, then inspect health/logs and manage releases. Runner roles, broker queues and deployment colors are not user-facing concepts. Public app traffic stays on the deployment VM; control-plane maintenance pauses management operations without stopping existing public apps. Private-app authentication retains the availability exception in section 6.

Commercial entitlements govern platform access, permitted machine provisioning and application policy; they must not create a different execution path. Managed pricing must account for idle VM rental, addresses, backups, retained images/artifacts, traffic and control-plane/support costs. No free or inactive account implicitly receives a billable VM. Record provisioning limits, cancellation/grace/retention policy, backup scope and who authorizes destructive deletion before the managed service launches. Paying for BYO SaaS does not imply Automaton can resize or delete the customer's hardware.

Treat SaaS capacity and isolation as release acceptance: authenticate every machine/operation/log request, bound per-account outstanding work and build concurrency, verify fair progress for another account while one is busy, reconcile provider costs/resources, and expose failed provisioning and overdue backups to operators. Open-source availability and the managed service share one codebase; the hosted offering provides installation, operations and support rather than a separate proprietary execution architecture.

## 2. What source review supports

Reviewed revision: `57941d9971f58409356a74cbcf2529292c2f8a61`. The original review also inspected uncommitted runtime-admission/shared-static work; that work was subsequently discarded at the user's request when this roadmap was moved into the repository. It is not part of the implementation baseline. This is a source review, not a production inventory or runtime acceptance result.

- [Worker registration](../../src/Automaton.Worker/Services/WorkerHostApplication.cs) primarily wires scheduled actions, shared-host storage policies, Runner registration/provisioning, registration notifications and CLI authorization cleanup. Removing the first group leaves a small set of responsibilities that can live in the control plane.
- [API registration](../../src/Automaton.Api/Extensions/ApiServiceRegistrationExtensions.cs) already runs CAP, background reconciliation and Hangfire. A separate executable is not required simply to consume CAP messages.
- [Deployment CAP subscriber](../../src/Automaton.Api/Messaging/DeploymentPackageRequestedCapSubscriber.cs) already uses a short notification handler instead of holding a consumer while remote deployment finishes. Preserve that principle.
- [Runner transport guide](../architecture/runner-management-transports.md) documents valuable durable operations, authorization and lost-outcome handling, alongside considerable broker/tunnel/API-color coordination. Preserve the guarantees while removing that topology.
- [App action execution](../../src/Automaton.Runner/Services/AppActionProcessExecutor.cs) and [process runtime execution](../../src/Automaton.Runner/Services/RunnerRuntimeProcessManager.cs) can launch local child processes. These must not become application execution paths in a privileged machine Runner.
- The repository already contains an [Apache-2.0 license](../../LICENSE). Repository visibility, release completeness and the rights to every distributed component still need review; a license file alone does not establish publication readiness.
- Inspected API, Worker and Runner projects target .NET 9. The API uses CAP 10.0.1 and Pomelo EF provider 9.0.0. Treat dependency modernization as a tested compatibility change, not a blanket version-number replacement.

## 3. Target architecture

```text
Studio / CLI / external coding tools
                 |
                 v
     Automaton control plane
     ASP.NET Core API + Studio assets
     CAP consumers + short reconciliation services
                 |
            MySQL + RabbitMQ
                 ^
                 | outbound authenticated HTTPS initiated by Runner
                 |
       One Runner on each deployment VM
       systemd service + durable local journal
                 |
       Docker containers + app data
       Traefik ingress + certificates
                 ^
                 |
      App visitors / company network
```

### One control-plane application; remove the Worker executable

Keep `Automaton.Api` as the host initially. Serve the compiled Vue Studio assets from that release under the control-plane origin; retain the Vue project for development. Keep the marketing website independent of the self-hosted bundle. Remove the separate Worker deployment, image, configuration, pipeline entries and project after migrating its retained responsibilities.

Put provider provisioning reconciliation, notification delivery and bounded cleanup in cohesive feature modules hosted by the API. Persist progress between short phases. Never keep an HTTP request or CAP handler waiting for a VM to boot or an app build to finish. App builds and execution occur on deployment machines.

Keep CAP and RabbitMQ internal to the control plane. CAP integrates with ASP.NET Core and supports transactional MySQL publication; a separate Worker is not a CAP requirement. Keep one messaging stack, and remove Hangfire/custom action scheduling once their consumers are removed. Do not replace CAP with a homegrown general-purpose job engine. [CAP integration](https://cap.dotnetcore.xyz/user-guide/en/getting-started/quick-start/), [MySQL transactions](https://cap.dotnetcore.xyz/user-guide/en/storage/mysql/).

The database owns desired machine/app state and operation progress. CAP delivers internal events; reconciliation finds unfinished database work after a lost wake-up. Delivery handlers must tolerate duplicates. Do not claim exactly-once effects. [CAP delivery and idempotence](https://cap.dotnetcore.xyz/user-guide/en/cap/idempotence/).

One process is the installation default, not a singleton correctness assumption. All reconcilers use database claims/version checks and bounded concurrency. Another replica can continue an operation. The same deployment must pass two-replica concurrency tests before release. Do not introduce a permanent leader or distributed lock service. A separate worker process can return only when measured load or fault isolation justifies it; do not build a second hosting mode now.

### One Runner product; remove Hosted/Infrastructure execution roles

Use one versioned Runner implementation for every deployment machine. Managed versus customer-owned is machine ownership/provisioning metadata, not a different executable, command protocol or runtime.

Install the Runner as a Linux systemd service. It performs a fixed set of typed operations: apply a release, stop/start an app, update routes, report health and perform explicitly authorized data recovery. No arbitrary shell endpoint, workflow engine, remote terminal or user-uploaded management script. SSH is only an optional bootstrap transport; normal operation needs no SSH session.

The Runner is a trusted machine-management component. Docker access is privileged; never give application containers its socket, configuration, credentials, host filesystem or namespace access. Do not run app commands directly as child processes of the service. [Docker daemon security](https://docs.docker.com/engine/security/).

Initial supported deployment host: a fresh dedicated Ubuntu 24.04 LTS x86-64 VM with a documented installation profile. Validate Docker/kernel/storage requirements. Do not claim arbitrary operating-system support. The control plane and untrusted customer apps run on different machines; a combined development environment is not the production reference topology. [Docker Ubuntu requirements](https://docs.docker.com/engine/install/ubuntu/).

## 4. One web-app contract

**The deployment artifact is an OCI image pinned by digest.** A small versioned manifest adds the internal HTTP port, readiness check, non-secret configuration, secret references, resource budget and declared persistent mounts. OCI already defines a portable image format; Automaton should own deployment policy rather than invent an application packaging format. [OCI image specification](https://github.com/opencontainers/image-spec).

- First vertical slice accepts a prebuilt image from an operator-selected registry.
- The useful release also accepts source plus a Dockerfile. An isolated BuildKit job on the account VM produces an image, pushes it to the configured registry and records its digest. Both inputs converge on the same release/deploy path.
- Studio and CLI submit the same API contract and show the same durable operation. A completed upload is not a completed deployment.
- One HTTP container per app/environment initially. Static sites use a small web-server image. Remove the separate API/static-files serving backend.
- Preserve stable app/environment IDs, ownership and CLI link identity where possible. Source remains editable source; a built image is disposable runtime output. No live-container editing or platform language-specific runner dependencies.
- Source repositories or local working copies remain the editing authority. The platform can retain immutable build inputs for a configured period; a hosted editor, source-sync daemon, runtime file browser and remix engine are not required to publish an app.
- External database/service connections are supported through configuration and secrets. An embedded database can use declared app storage. Managed database clusters, arbitrary Compose stacks and sidecars are deferred. Inventory existing dependencies: an app needing multiple services must receive an explicit migration design or remain a cutover blocker.

Build inputs are untrusted. Bound CPU, memory, PIDs, wall time and disk use; allow only supported build options, and disallow privileged/insecure build entitlements, host mounts and access to Runner credentials. Evaluate an isolated rootless BuildKit configuration on the supported VM image and prove its confinement. Never copy an unsafe privileged example into production merely because the image is called rootless. Registry pulls, source fetches and build traffic need an explicit policy for access to internal networks and cloud metadata services.

Use BuildKit secret mounts for authorized build credentials, not build arguments or copied files. A malicious build can still exfiltrate a secret it is deliberately given; grant only app-scoped credentials and never provider/control-plane credentials. [Build secrets](https://docs.docker.com/build/building/secrets/), [BuildKit rootless support](https://github.com/moby/buildkit/blob/master/docs/rootless.md).

Deployment is deliberately conservative: resolve/pull the immutable image first, then serialize changes for the target. For writable app data, stop the old writer before starting the new release. Verify readiness, apply the route and record the result. Accept a short deployment interruption; do not add per-app blue-green machinery to this release.

Retain the previous image/configuration for an explicit revert. Reverting an image does not revert database migrations or file changes. If failed startup may have changed data incompatibly, report recovery required instead of automatically starting an incompatible old image. No arbitrary pre/post-deploy migration hooks in this release.

## 5. Durable control without the current transport complexity

The Runner polls one stable HTTPS control-plane URL, using a short-lived enrollment token followed by a machine-bound, revocable credential. Poll with bounded backoff and jitter, respecting `Retry-After`. Remove Runner-facing RabbitMQ queues/accounts, SignalR registration dependencies and API-color identity from the execution protocol.

Use one compact operation envelope with operation ID, machine identity, target identity when applicable, generation and a typed payload. Keep machine and app lifecycle logic in their respective owners; sharing an envelope does not require a generic workflow interpreter.

Required behavior:

1. The API commits desired state and the durable operation before dispatch becomes possible. Idempotency keys prevent duplicate user requests from creating duplicate resources.
2. Any API replica can return pending work and accept an authorized result. No sticky session owns an operation.
3. The Runner durably records acceptance before mutation and journals resource identities/phases. It resubmits saved terminal results after a lost acknowledgement.
4. Serialize mutations per deployment target. Machine replacement/deletion takes a machine-wide barrier. Do not serialize unrelated health reads behind a long build.
5. On restart, reconcile actual labeled resources with the journal. A timed-out API lease alone is not evidence that remote work stopped. Never start a second writer on a replacement machine until the former writer is fenced or its stopped state is established.
6. Report uncertain, non-reconcilable outcomes explicitly; do not retry destructive work blindly. Keep desired state, observed runtime health and Runner connectivity distinct in Studio.
7. Fence results from revoked identities and old generations. Retain enough audit history to explain rejected results and interrupted migrations.

Initially use polling for Studio operation progress and bounded log tails too. Remove SignalR completely if the feature-removal inventory finds no retained consumer. Do not introduce WebSockets, server-sent events or long polling until ordinary polling shows a measured need.

## 6. Ingress and machine autonomy

**Choose direct ingress on each deployment VM for the first release.** Keep Traefik, which Automaton already uses. The Runner writes validated app routes through the file provider; Traefik need not receive the Docker socket. Persist certificate state. Configure automatic public certificates or operator-supplied certificates. [File provider](https://doc.traefik.io/traefik/reference/install-configuration/providers/others/file/), [ACME requirements](https://doc.traefik.io/traefik/reference/install-configuration/tls/certificate-resolvers/acme/).

**Traefik is the platform's sole reverse-proxy technology.** Use it for control-plane ingress and one local ingress instance per deployment VM. Remove the redundant Nginx app-proxy layer, legacy Nginx route templates/reload scripts and the API's central app-route file generator. Serve Studio assets from the API as specified above. An application's own image may use Nginx as its web server, and a customer's existing external gateway remains their choice; neither adds another Automaton-managed proxy implementation.

The control-plane database remains authoritative for desired routes. Each Runner generates only its machine's routing configuration, validates it, replaces it atomically and verifies that Traefik applied the intended routing and authentication rules. Persist the last accepted configuration locally so public apps keep serving during a control-plane outage. These files are derived configuration, not a second editable source of truth. Include stale-route removal and interrupted-update recovery in acceptance tests.

- Managed public VMs receive a platform hostname through configured DNS automation, plus optional verified custom domains. Use per-machine DNS delegation/records rather than assuming one global wildcard can route to every VM.
- A public customer VM uses the same ingress with customer-controlled DNS and reachable ingress ports.
- An internal customer VM serves its company network through private DNS, supplied certificates and its existing LAN/VPN/gateway. Outbound Runner connectivity does not make that app public.
- Automatic public tunnels for machines behind NAT are deferred. Remove the current API-hosted tunnel and reconnect/compensation machinery. Existing tunnel-dependent applications require an approved direct/private/gateway destination or block cutover.
- Public app traffic bypasses the control plane. Public apps continue serving through a control-plane restart or management-network outage, while new deployments wait.
- Preserve existing private-app authorization for migrated apps through a restricted authentication check at ingress, failing closed when its authority is unavailable. Never make a private app public as a simplification. These protected apps may depend on control-plane availability; document that exception. A future local OIDC gateway can provide a different availability contract.
- Do not expose app container ports independently of ingress. Validate host/domain ownership, prevent route collisions, reject forged proxy identity headers, and verify direct-origin access cannot bypass authentication.

For rollout, lower DNS TTL in advance and retain old data offline. DNS cutover is not atomic: old origins must return maintenance responses rather than resume writing. Preserve existing names when the configured DNS permits it; record any changed URLs explicitly. No central edge compatibility route remains after the cutover window.

## 7. VM lifecycle, resources and recovery

Use the existing account as the ownership boundary initially, preserving its current organization/permission checks. One managed primary VM per paid account is the initial product policy; the data model may associate multiple customer machines with an owner. Do not redesign organizations and billing during this migration. Machine transfer between owners is deferred.

Start with manual enrollment, then one provider adapter and a reviewed SSH bootstrap for fresh customer machines. The first managed provider is a stage-0 decision based on API behavior, availability, cost and support; do not build a generic provider plugin marketplace. Persist a correlation identity before creating a billable VM, reconcile ambiguous create results, and record orphan cleanup. An unresponsive VM is not authorization to delete it.

For customer bootstrap, verify SSH host identity through an independently supplied fingerprint, display concrete installation changes, make interruption resumable, and remove temporary bootstrap access after successful enrollment. Never repartition or erase a customer disk automatically. Reject unsupported existing workloads or storage layouts with an actionable explanation.

Machine deletion, app deletion and customer disconnection are different operations. Disconnection revokes Automaton access and leaves customer machines/data intact. Managed VM deletion requires confirmed data disposition, no live writers and a reconciled provider deletion result. Keep billing cancellation/retention policy explicit; inactivity alone never implies permission to destroy data.

Remove the shared-host XFS quota, tenant cgroup parent and placement-reservation machinery. Keep simple per-app CPU/memory/PID limits and bounded build concurrency. Admit workloads only when their configured budgets leave OS/Runner headroom; measure that headroom on the supported image. A shared vCPU allocation is not a dedicated physical core guarantee. [Container resource controls](https://docs.docker.com/engine/containers/resource_constraints/).

Disk safety needs a concrete installation contract: place app data, images/build caches and container writable layers on a bounded workload filesystem, separate from the OS and Runner journal. Keep system logs bounded too. Initial managed images must provide that layout; customer enrollment verifies an existing suitable layout or requires operator preparation. This protects recovery capacity without restoring the old per-tenant XFS accounting service. Test full blocks and exhausted inodes, not only a low-space alert. Never garbage-collect declared app data as cache.

App volumes survive container updates and VM reboots. One VM is not high availability. Before migrating stateful production apps, establish an off-machine backup destination, encrypted backups, retention and a verified restore to another machine. Stop writers for a generic file backup; databases outside that scope require their own consistent backup. Reuse a maintained tool such as restic through a narrow typed procedure. A backup dashboard and arbitrary backup hooks are deferred. [Restore behavior](https://restic.readthedocs.io/en/stable/050_restore.html).

Record backup ownership, frequency, acceptable data-loss window and restore-time target for the initial service. Fixed backup scheduling is platform housekeeping, not a reintroduced user workflow engine. Protect recovery images from registry garbage collection. Back up the control-plane database, encryption/key-ring material, retained artifacts and required registry content as well as app volumes; demonstrate that restored secrets can actually be decrypted. A data-only copy is not a complete platform restore.

The first managed release offers fixed VM sizes. Self-service resize and live migration are deferred; a documented replacement-and-restore maintenance procedure handles necessary size changes. A VM disk allowance includes platform overhead. Charge managed infrastructure separately from a customer's BYO entitlement; no idle/free VM allocation until the cost policy is approved.

## 8. Self-hosting and open-source release

Self-hostability is an acceptance requirement from the first vertical slice, not a later rewrite.

The reference control-plane bundle uses Docker Compose with four services: the API serving Studio, MySQL, RabbitMQ and a TLS proxy. CAP is a library inside the API, not another container. Use one production installation recipe with development overrides; remove redundant historical deployment recipes after conversion. Single-host Compose is a supported operating model, not an HA promise. [Compose production guidance](https://docs.docker.com/compose/how-tos/production/).

A registry is a required endpoint for image deployment, not necessarily another bundled service. Accept an existing public/private OCI registry; offer a documented optional local registry for fully internal installations. Source-build inputs use a configured durable artifact store with retention. Single-node installs can use a mounted volume; multiple API hosts must use a genuinely shared store and key ring. No artifact may depend on the replica that received it. Test those semantics; do not advertise multi-host HA merely because operations use a database.

Required installation/release work:

- Bootstrap a local administrator securely; disable unrestricted signup by default. Keep existing identity/authorization and add company OIDC as a later bounded feature. No required Automaton-hosted sign-in.
- Configure the instance URL, private/public app domain policy, CA trust, SMTP if used, artifact/registry endpoints, encryption key storage and backup destination. No deployment-specific names compiled into binaries.
- Keep billing/provider purchase features disabled by configuration for self-hosted use, with full app deployment available. This is a product configuration, not a legacy execution backend.
- Publish pinned control-plane images, Runner installation artifacts and CLI packages from CI, with checksums, provenance, an SBOM and signature verification instructions. Retain one release authority; adapt the existing pipelines rather than adding an unofficial publishing path. Public GitHub CI is a separate repository-governance choice.
- Provide an explicit preflight/migration command, version manifest, upgrade procedure, backup/restore procedure and uninstall consequences. Never run destructive schema migrations opportunistically on every API startup.
- A control-plane upgrade can use a short maintenance restart. Existing public apps remain served by their VMs. Remove API-color orchestration and duplicate blue-green state machinery from the new reference deployment. Breaking schemas still require a restore/forward-repair plan.
- Document data flows: the configured control plane receives app metadata, telemetry, requested logs, deployment secrets and optionally source build inputs. App persistent data stays on its machine except configured backups. Fully self-hosted use can keep these services internal. Hosted control plane plus BYO compute is not a promise that all information stays on premises.
- Demonstrate operation without contacting Automaton-owned services. Fully air-gapped builds/installation are a later capability requiring mirrored packages, base images and trust material; do not imply it from self-hostability.
- Review repository/history and release artifacts for secrets and private assets, include third-party notices and contributor/security guidance, and verify redistribution rights before any publication. Keep the existing Apache-2.0 licensing direction unless the owners explicitly choose another policy. Publication and visibility changes remain separate user decisions.

## 9. Modernization with a purpose

Upgrade the maintained C# solution to .NET 10 LTS. .NET 9 reaches end of support on 10 November 2026; .NET 10 is supported through 14 November 2028. Establish the EF provider/CAP/ASP.NET compatibility matrix first; never assume matching major numbers or a newer target framework establish compatibility. Keep MySQL rather than coupling this migration to a database-engine switch. Pin a supported MySQL release and validate both fresh schema and import. [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy), [Pomelo compatibility](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql).

Keep Vue 3/TypeScript and generate its API models from OpenAPI. Keep business decisions in API feature modules; CLI and Studio are clients. Use ordinary dependency injection and typed provider/runtime boundaries. Remove obsolete projects/packages rather than replacing them with a new layer for each old layer.

Use structured logs and OpenTelemetry instrumentation with operation, machine and deployment IDs. Start with health endpoints and a small operational view for pending-operation age, VM capacity, failed deployments, certificate expiry and backup status. Telemetry exports are optional operator configuration; avoid mandatory observability infrastructure. [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/).

Do not introduce Kubernetes, Swarm, service meshes, event sourcing, a custom broker, a generic workflow engine, an operator framework or an infrastructure plugin SDK. A standard component is useful when it removes code or supplies a required guarantee; popularity alone is not a reason to add it.

## 10. Minimal state and cleanup ownership

### Mandatory implementation requirements

The following are required implementation outcomes, not optional recommendations. The replacement release must:

1. **Remove Actions and workflows**, including custom steps, user-defined scheduling, executors and their API, CLI, MCP and Studio surfaces.
2. **Remove Hangfire**, including its packages, server/dashboard registration, job dispatch, recurring-job configuration and obsolete persisted job state through the controlled migration. Retained platform housekeeping runs through the control plane's CAP and bounded reconciliation services.
3. **Remove host-process app execution.** App runtime and build code execute only inside the supported isolated container/build environment. No legacy fallback may launch application commands under the machine Runner's host identity. Typed Runner operations may still invoke trusted platform tools such as Docker.
4. **Remove the separate static-hosting backend.** Static web apps use the same OCI release, deployment, ingress and authorization path as other web apps. The control plane may still serve its own Studio assets.
5. **Preserve the mechanisms protecting ownership, secrets, persistent data and recovery.** Retain or replace their implementations with verified equivalents: account/app/machine authorization, scoped credentials and secret protection, stale-owner fencing, durable operation journals, persistent-volume ownership, consistent backups and tested restore. Simplification must not weaken these guarantees.
6. **Standardize platform ingress on Traefik only.** Remove the redundant Nginx proxy implementation and central app-routing generator. Use Runner-owned local routing files on each deployment VM, with validated atomic updates, authentication enforcement and verified recovery as defined in section 6.

Completion requires removal of the associated live code, registrations, dependencies, configuration, schema and product surfaces, plus acceptance evidence that the preserved guarantees hold. Disabled legacy paths, compatibility switches and untested replacement safeguards do not satisfy these requirements. Stage 4 must demonstrate this gate before migration rehearsal.

### State ownership and removal inventory

Retain identity/accounts, apps/environments, authorization, secrets and necessary audit data. Converge runtime state around a deployment machine, an immutable app release, desired/observed target state and durable operations. Volume identity belongs to its machine and app target. Avoid several competing records that can each decide which Runner, release or volume is authoritative.

A machine holds its owner, management policy, optional provider identity, Runner credential binding, capacity and health. A release holds the image digest and configuration revision. An operation holds its type, target, generation, phase and durable result. Historical migration mappings belong in one offline conversion ledger, not a permanent scheduling service.

The new release must remove:

- `HostedRunnerHostPlacementService`, account XFS allocation/enforcement/verification, shared runtime-port pools, and shared-static serving.
- Infrastructure-Runner-to-Hosted-Runner provisioning, sibling-container execution and associated `HostContainerExecution*`/brokered runtime routes. Retain direct Docker operations under the one Runner.
- Old host-process application execution; Actions/workflows/custom steps/schedules and their UI/API/MCP/CLI execution commands; infrastructure Python actions replaced by typed operations.
- Runner broker credentials/queues, old registration/provisioning roles, SignalR app tunnels and color reconnect/compensation code.
- Worker/Hangfire hosting, retired consumers and scheduled storage jobs. Move only retained notifications, cleanup and machine reconciliation.
- Runtime source synchronization and general file-operation transports that exist solely for removed editing/actions features. Retain deployment input transfer and declared data recovery under narrow contracts.
- Old schema fields, dependency registrations, packages, Docker/Compose services, environment variables, tests and docs that serve only the removed behavior.

Review dependencies before deleting entire files or projects; these names identify responsibilities. Preserve or rewrite tests for ownership, retries, immutable deployment, secret handling, persistence and recovery. Delete tests that merely require an intentionally retired product capability.

## 11. Ordered roadmap

Each stage produces working evidence and a smaller final codebase. Implementation stays in the existing checkout/branch. Check the working tree before implementation and preserve unrelated changes unless the user explicitly authorizes discarding them. No production rollout occurs before all cutover gates pass.

### Implementation readiness and planning depth

This roadmap is sufficient to start Stage 0 and prepare the first implementation slices. It is not an exact schema, wire-protocol specification or production cutover runbook. Resolve those contracts immediately before the dependent work. Do not delay the first working VM for a complete design of every later feature, and do not begin destructive cleanup or production conversion from architectural prose alone.

Use the ordered slices below as the initial implementation backlog. Complete the mandatory pre-stage development step below before the dependent coding tasks. Keep decisions and completion evidence with the relevant stage in this roadmap; link a focused schema/protocol/runbook document only when the detail would obscure the roadmap. Do not create a second competing execution plan or a generic orchestration framework for tracking this work.

Keep commits small and independently reviewable. A development branch may temporarily contain old code while replacements are completed, but no mixed backend release, production feature switch or executable compatibility path is an accepted outcome. Remove obsolete code as each replacement becomes testable; Stage 4 is the final deletion audit. Interim artifacts are for disposable acceptance only until the complete release passes its cutover gates.

For C# slices, capture affected-project analyzer diagnostics before editing, run the affected non-incremental Debug build afterward, and run the all-project non-incremental Debug build plus affected tests before handoff, as required by `AGENTS.md`. Record pre-existing unrelated failures without suppressing them. Studio changes require its type/build and visual checks; touched production shell scripts require syntax checks. A passed test or build does not imply live VM or migration acceptance.

### Mandatory pre-stage development step — Specify the next stage

**When:** perform this preparation before starting each stage, using the completed prerequisite work and its acceptance evidence. For Stage 0, define the inventory scope, access requirements and expected decision records before beginning the inventory. For Stage 1, turn Stage 0's first-slice contracts into concrete implementation specifications. Immediately after Stage 1.6 passes, prepare Stage 2 from the working foundation. Repeat for Stages 3–6 before their implementation, rehearsal or execution begins. Preparation may include source inspection and disposable experiments needed to resolve a design choice; it does not authorize production changes.

**Required deliverable:** a concise stage development specification, recorded with that stage or linked from it, covering the applicable items below. These are implementation inputs, not another high-level roadmap.

1. **Scope and dependencies:** the observable result, required earlier evidence, affected projects/files, retained responsibilities, exact deletion scope and explicit exclusions. Break the stage into ordered, reviewable coding tasks.
2. **Schema:** proposed entities/tables and fields, ownership and relationships, keys/indexes/uniqueness constraints, concurrency controls, secret representation, and required migration/import changes. Explain where authoritative state lives and how fresh and migrated installations reach the same result. Mark this item not applicable when the stage changes no schema.
3. **API and Runner contracts:** concrete routes/methods, request/response models, authorization, validation/error behavior, idempotency and cancellation semantics. Specify operation states/transitions, typed Runner payloads, protocol version expectations and journal changes where affected. Include representative payloads and expected outcomes before implementing consumers.
4. **Failure and recovery:** duplicate delivery, concurrent replicas, stale owners/credentials, interrupted execution, unknown outcomes and resource cleanup. Define what can be retried, what must be reconciled and what requires explicit recovery. For stages introducing provider, storage or routing behavior, specify those external side effects and their confirmation/fencing rules.
5. **Verification:** fixtures, affected tests, analyzer/build checks and measurable acceptance criteria. Identify necessary live/disposable checks, data-preservation checks and any external access or unresolved product decision. Map each criterion to the task that will demonstrate it.
6. **Completion record:** list the decisions made, the remaining questions and which tasks they block. Record where the implementation and acceptance evidence will be added; mark preparation complete only when the dependent tasks have sufficient concrete specifications.

**Entry gate:** no dependent coding task starts with unresolved schema, ownership, security, protocol or recovery decisions that determine its behavior. Unrelated later-stage choices do not block an otherwise specified task. The implementing agent resolves routine technical choices within the agreed architecture; this preparation step is not a recurring request for user approval. Obtain user input when a material product choice is unresolved, and keep the existing explicit authorization requirements for production operations and resource purchases.

Before each coding task, check that the specification still matches the code and prior results. If implementation evidence changes a contract, update the specification and affected acceptance criteria before proceeding with its consumers. Refine later stages from completed work rather than freezing speculative details for the entire migration up front.

### Stage 0 — Freeze scope and inventory

Inventory accounts, apps, environments, private/public routes, tunnel reliance, databases/volumes, source, secrets, active operations and Actions/workflow-only content. Categorize every item as migrate, export/archive or explicitly retire. Record all unsupported apps and dependency blockers; no silent exclusion.

Choose one provider, cost/entitlement policy, supported machine image/storage layout, ingress/DNS scheme and registry/artifact endpoints. Confirm whether any production user depends on deferred capabilities. Record required URL changes and maintenance expectations. Produce a short decision record and deletion inventory; postpone exhaustive conversion code until the vertical slice works.

**Gate:** every retained feature and data category has an owner and destination; the machine/control-plane contracts are precise enough to implement; no unresolved assumption can silently expose data or destroy a workload.

Implement Stage 0 as these ordered deliverables:

1. **0.1 — Baseline and dependency inventory.** Record the implementation revision and inspect the retained API, Worker registrations, Runner execution/transport paths, CLI, Studio, schema and release scripts. Produce a keep/replace/delete list with concrete files and their consumers. Inventory current workloads and data through supported read-only inspection when access is available; record unavailable inventory explicitly rather than inventing counts. Identify apps requiring tunnels, private authentication, multiple services or nonportable data.
2. **0.2 — Freeze the first-slice contracts.** Specify machine ownership/enrollment/revocation, image-release manifest, desired/observed deployment state, operation transitions, generation fencing, cancellation/unknown outcomes and persistent-volume identity. Define the minimal API payloads and database constraints, plus the Runner journal format/version. Locate each responsibility in an existing feature or a narrowly scoped new feature. Keep provider-specific details out of app contracts. These are the concrete reviewable inputs to Stage 1, not another broad architecture proposal.
3. **0.3 — Record installation and operating decisions.** Fix the supported VM/storage profile, first test registry, artifact/key-ring location, Traefik route/certificate policy and isolated test environment. Record the initial provider and managed/BYO responsibility, entitlement and cost policies when settled. Unresolved commercial/provider choices block managed provisioning, not .NET modernization or a manually enrolled disposable VM. Keep all production migrations and resource purchases separate from source implementation.
4. **0.4 — Establish acceptance fixtures and traceability.** Select a small stateless web image, a persistent-data web image and a static image, with two independent account identities. Specify observable results for restart, lost acknowledgement, conflicting requests, stale credentials and control-plane outage. Map each mandatory requirement in section 10 to its implementation slice and acceptance gate. Fixtures must use synthetic data and explicit disposable-resource configuration.
5. **0.5 — Validate the audience, publishing experience and build-versus-adopt choice.** Define the first customer segment and test the whole outcome with its representative users: start with an app made by an external AI builder or developer, get it running, share it with the intended audience, manage access, update it and recover it. Compare that flow with an existing AI app builder and an existing deployment platform; record actual friction, responsibility gaps, user preference and willingness to use/pay for the proposed service. Investigate whether an existing deployment engine could support Automaton's intended managed/BYO/on-premises experience. Decide whether the initial release needs app discovery or only owner-controlled sharing. Do not justify a repository-wide rewrite solely from enthusiasm for AI-generated apps or from architecture simplification.
6. **0.6 — Choose the installation and administration boundary.** Prototype and compare a single-server appliance with a control-plane-plus-host-node installation on disposable VMs. Include clean install, upgrade, backup/restore, control-plane outage behavior, resource contention, public app availability, safe uninstall, and onboarding a user who has an existing VM. Record which one is the default and which (if any) is an explicitly supported later mode. Define the first machine-management guarantees and their operator: health/capacity reporting and verified backups are candidates; SSH/firewall changes, unattended OS updates, reboots and arbitrary existing-machine adoption require separate threat, recovery and support decisions. The gate is a reviewable install-and-publish flow plus an explicit failure/responsibility model, not merely a successful installer script.

Stage 0 produces actual inventory/contract and product-validation decisions before it is marked complete. Its production-dependent findings may remain open during isolated development, but every such finding must be resolved before the relevant migration or launch gate. Stage 1 can test the technical deployment foundation on disposable infrastructure. The product-validation result is a go/no-go before expanding into the broader Stage 2 publishing experience or committing to full legacy conversion; if an existing platform meets the audience's needs, narrow Automaton to the demonstrated gap or adopt that platform. No existing production acceptance is implied by writing this backlog.

### Stage 1 — Modern foundation and one deployable VM

Validate the .NET 10 dependency matrix. Build the small self-hosted control-plane bundle and one manually enrolled Runner. Deploy a prebuilt image with a domain, health check, limits and a persistent mount. Implement HTTPS work polling, journal recovery and local ingress. Prove app/container confinement and machine headroom. Use the old implementation only as a reference; the prototype is a slice of the new implementation, not a second production backend.

**Gate:** install from release artifacts on clean infrastructure; publish through CLI and Studio; survive control-plane/Runner restarts and lost result acknowledgements; reject a second account's access. Public serving survives a control-plane outage. Two API replicas cannot execute conflicting mutations.

Implement Stage 1 in these reviewable slices, with each slice depending on the previous one unless its prerequisites are independently satisfied:

1. **1.1 — Supported runtime and control-plane package.** Validate and apply the .NET 10/EF provider/CAP/MySQL dependency matrix. Produce the reference API/Studio, database, broker and Traefik bundle with explicit durable storage, secret configuration and health checks. Keep unrelated runtime rewrites out of the dependency change. Acceptance is a clean build and disposable installation with identity and database access; no hidden Automaton SaaS dependency.
2. **1.2 — Machine identity and durable desired state.** Implement the agreed machine/release/operation records, fresh-schema setup, account authorization and manually issued enrollment. The credential is bound to one machine/owner and can be revoked. Implement idempotent request acceptance, conditional claims and version checks using real MySQL tests with concurrent callers. Do not create provider VMs yet or add an old-schema adapter.
3. **1.3 — HTTPS Runner and recovery journal.** Install the one Runner as a systemd service, authenticate its polling/results/heartbeat calls and persist accepted work/results locally. Implement generation checks, duplicate-result handling and interrupted-operation reconciliation. Test lost acknowledgements, process death and a result arriving at another API replica. Connectivity loss must not mark a still-serving deployment failed or authorize destructive replacement.
4. **1.4 — Safe image deployment and local ingress.** Pull a digest-pinned fixture image, create only owned/container-labeled resources with limits and allowed mounts, serialize deployment changes, verify readiness and apply Traefik routes atomically. Persist the accepted release/route state and reconnect after a VM reboot. Verify public access, foreign-account management denial, stale-route removal and no direct-origin authentication bypass where protection is configured. No host-process app fallback or Docker-socket exposure.
5. **1.5 — Thin CLI and Studio workflow.** Expose connect-machine, deploy-image, operation status, stop/start and bounded health/log reads through the same API. Studio displays machine connectivity separately from app health, and the CLI waits for a terminal deployment result. Keep provider purchases, source builds, Actions and infrastructure-admin screens out of this slice. Generate typed clients/models from the agreed API contract.
6. **1.6 — End-to-end acceptance and evidence.** Run the clean-install/publish/restart/recovery sequence with two accounts and two API replicas on disposable infrastructure. Verify a persistent marker across app update and VM reboot, rejection of stale/revoked credentials, and continued public serving while the control plane is unavailable. Record exact release artifacts, results and unresolved limitations. This gate proves the foundation; it is not the production migration or the complete managed offering.

After 1.6, detail Stage 2 using the demonstrated contracts. Expand each later stage before starting it, preserving its existing scope and gates. The exact offline importer and cutover runbook belong after the new deployment path is proven, while the workload inventory remains an early input.

### Stage 2 — Useful publishing and data recovery

Add the Dockerfile/source path through isolated BuildKit and the selected registry, immutable release history, bounded logs, private-app protection and domain validation. Finish stop/start deployment, explicit image revert semantics and secret handling. Implement off-machine backup and restore for supported persistent data. Publish runtime language fixtures covering at least two different stacks plus a static image.

**Gate:** source-to-HTTPS works without an operator editing the VM; secrets never appear in images/logs through platform injection; persistent data survives update/reboot; a full/inode-exhausted workload filesystem leaves the Runner recoverable; a stopped-writer backup restores on a fresh VM; private origins fail closed. Untrusted builds cannot access the Runner or another app's data.

### Stage 3 — Managed provisioning and customer setup

Implement the first provider's create/reconcile/delete flow and the SSH bootstrap using the same enrollment contract. Add basic capacity/health views and explicit ownership/deletion consequences. Keep resize and general OS management out. Package independent self-hosting, backup and upgrade instructions; demonstrate an internal installation using internal domains and registries.

**Gate:** duplicated/time-out provisioning requests create at most the intended billable resource after reconciliation; interrupted setup resumes; customer disconnection leaves the server intact; provider failure does not trigger destructive cleanup. The complete product works without an Automaton SaaS dependency.

Before declaring the SaaS offering ready, exercise both managed and BYO onboarding through the same Studio/API, account entitlements, cancellation/retention outcomes, operator recovery and cross-account workload fairness. Publish the responsibility and backup scope for both offers. Independently validate the same release installed on a company's private network without Automaton-hosted identity, billing, registry or control-plane services. Do not retire or repurpose the current application server until Stage 6 has reconciled its workloads and recovery copies.

### Stage 4 — Delete legacy code and implement offline conversion

Complete the cleanup inventory and contract the schema. Move retained Worker responsibilities into API modules; delete its project and release path. Remove unsupported execution features, both legacy Runner roles and old transport/routing services. Update CLI, Studio, Admin documentation and architecture inventories to the new behavior.

Implement a deterministic importer from a frozen old database/data export into a clean new schema. Preserve IDs/permissions where meaningful, re-enroll machines with new scoped credentials, and produce explicit mappings for every retained datum. Build and validate OCI images for each retained app; do not smuggle host-process execution into the new runtime to make imports pass. Historical EF migrations may remain provenance, but no new production process runs against the old schema.

Checkpoint imports in the migration ledger and prove that resuming cannot duplicate app/machine assignments. Retain the old encrypted secret material and required keys until secure conversion is verified; do not put plaintext secrets in migration manifests or logs. Keep the importer out of the normal production request/Runner paths.

**Gate:** dependency searches find no live legacy consumers; fresh install and imported install behave identically; every retained app has a tested image, data destination and serving/authentication outcome. Archived Actions/source/history can be retrieved without reinstating an executor.

### Stage 5 — Rehearse the complete migration

Restore a representative production copy onto isolated infrastructure. Freeze old writers, export consistent data, import the new schema, restore workloads, apply routes and verify all accounts. Measure copy/build/restore time, inspect actual storage and establish the maintenance window. Test failure before reopening writes, and restore the old release/database/data as one consistent set.

**Gate:** record evidence for ownership, all environments, app data, source archives, image provenance, public/private routes, credentials, build isolation, restore and migration resumption. Include DNS propagation behavior and old-origin maintenance fencing. Resolve every unsupported production app before scheduling cutover.

### Stage 6 — One maintenance cutover and retirement

In the explicitly authorized window, stop admissions and all old background/execution writers; stop app writers; capture the final consistent backup/export. Import the new schema/data, deploy the new release, validate all workloads/authentication and reconcile DNS/routes. Reopen writes only after acceptance. Old agents remain revoked and stopped. No mixed protocol fleet or fallback backend is supported.

Before reopening writes, recovery restores the complete old release/database/data/routes set. After new writes begin, the supported strategy is forward repair or recovery from current backups. Returning to the old model would require a separately verified reverse conversion; do not promise one or silently restore stale data.

Retain old data offline for the agreed recovery period. Delete old volumes, credentials, queues and provider resources only after their ownership and retention conditions are satisfied. Reconcile every billable resource. Record the release, restored/migrated counts, unresolved limitations and final retirement evidence.

**Gate:** the production fleet and release contain only the new architecture; all retained apps pass their serving/data checks; no retired consumer or old writer can become active. The operator can install, upgrade and restore the same release on a separate site.

## 12. Deferred work and success criteria

Defer automatic OS maintenance, SSH key administration, management apps, custom Actions/workflows, live resize/migration, HA app replication, arbitrary Compose stacks, managed database services, automatic public tunnels, multi-provider procurement, Kubernetes, enterprise SSO, air-gapped distribution and a built-in AI execution platform. Ordinary OS security maintenance still needs an assigned operator and runbook; deferring its automation does not remove that responsibility.

An optional Git-provider integration or buildpack can be added later if it produces the same OCI release. Optional machine-management features must use typed authorized operations. They must not bring back arbitrary host script execution as the platform's foundation.

Judge the release by these outcomes:

- A user publishes a healthy HTTPS app through one understandable workflow.
- Managed and customer-owned VMs use exactly the same Runner/runtime.
- A company installs and operates Automaton without Automaton cloud services.
- Public apps keep serving while the orchestrator is restarted.
- Account/app isolation, persistent data and recovery are demonstrated under failure.
- The repository has fewer execution paths, deployable services, transport mechanisms and operational runbooks.

The next implementation deliverable is **Stage 0 followed by the Stage 1 vertical slice**, not a full rewrite or a production migration. All stages are pending. This document changes the recommended plan only; current source, production behavior and Admin instructions have not been changed or validated by execution.
