# Managed platform completion plan

> Historical assessment: the current execution order and milestone status are maintained in the [hosting and platform completion roadmap](../roadmap/managed-platform-completion-plan.md). The assessment below is retained as its original source-review baseline; it does not describe the latest rollout status.

## Assessment baseline

- Assessed repository: `rzvdaniel/automaton`, `main` at [`2b41c263`](https://github.com/rzvdaniel/automaton/commit/2b41c2636ffae126d4e3dd9ec2cefe8d3577dd71).
- Assessment date: 2026-09-26.
- Method: source, test inventory, pipeline configuration, and operational documentation review. No application tests, host experiments, payment-provider tests, or production checks were executed for this assessment.
- This is a completion plan. Existing code demonstrates mechanisms; operational acceptance requires recorded results against the deployed image digest and configuration.
- Earlier Stages 1–3 are the historical foundation. The old “Stage 4 next; Stages 4–10 unimplemented” checklist no longer describes current main accurately. Several later-stage mechanisms now exist.
- The earlier architecture trackers are absent from this main revision. This document provides a new baseline rather than restoring their implementation status.

## Goal and release scope

Deliver a managed-hosting release in which an account receives the resources promised by its effective subscription, every supported workload has bounded resource consumption, usage is understandable, billing transitions preserve data, and operators can recover the service.

The scope has four workstreams:

1. **Tenant resources and accounting:** policy resolution, admission, persistent storage, runtime/build write paths, aggregate compute, usage, and policy changes.
2. **Infrastructure reliability:** durable operations, capacity, host lifecycle, recovery, backups, observability, security, and safe releases.
3. **Pricing and billing:** an approved product catalog, economics, correct payment lifecycle, and consistent customer-facing promises.
4. **Existing app lifecycle:** creation/import, build, deploy, publication, runtime supervision, update, stop, removal, data retention, and restoration across supported deployment modes.

“All app lifecycle work” means completing these existing supported flows against the acceptance matrix below. New runtime engines, multi-region failover, arbitrary live migration, autoscaling, and new collaboration products require separate scope decisions.

Managed tenant resource guarantees apply to Automaton-managed infrastructure. Self-hosted Runners retain their own host-resource authority; ownership, target isolation, deployment status, cleanup, and routing still require lifecycle correctness. Frontend publication and control-plane file storage need their own explicit accounting and capacity rules.

## Current implementation and remaining gaps

| Area | Evidence on main | Assessment / remaining work |
|---|---|---|
| Subscription resource policy | [Resolver](../../src/Automaton.Api/Services/SubscriptionResourcePolicyResolver.cs), [policy](../../src/Automaton.Api/Models/SubscriptionResourcePolicy.cs), [capabilities](../../src/Automaton.Infrastructure/Models/PlanCapabilities.cs) | Implemented through effective plan → administrator-managed specification → validated CPU, memory, disk, PID and app limits. Keep this authority unless a product decision requires independent account budgets. Verify every admission path and concurrent request. |
| Stable storage identity | [Allocator](../../src/Automaton.Api/Services/AccountStorageProjectAllocationService.cs), [provisioning](../../src/Automaton.Api/Services/HostedRunnerProvisioningService.cs) | Account allocation survives Hosted Runner replacement/deletion. Stable project identity alone does not aggregate different filesystems or provide data movement. |
| Hosted Runner storage and compute | [Provision action](../../system-apps/automaton-infrastructure/actions/provision-hosted-runner/main.py), [reprovision action](../../system-apps/automaton-infrastructure/actions/reprovision-hosted-runner/main.py) | XFS enforcement checks, system/user project assignment, same-filesystem checks, hard-limit verification, shared-project conflict guard, and cgroup v2/systemd slice setup already exist. Complete drift, inheritance, restart and failure acceptance. |
| Placement | [Host placement](../../src/Automaton.Api/Services/HostedRunnerHostPlacementService.cs) | Checks host eligibility/capacity and conflicting same-account disk snapshots. An account-wide storage domain and cross-runner budget are not established by this guard. Reservation accounting needs pending-operation/concurrency acceptance. |
| App persistent storage | [Managed runtime creation](../../src/Automaton.Runner/Services/HostContainerExecutionService.Runtime.cs) | Managed app data and declared mounts use subpaths of the Hosted Runner user volume. Artifact mount is read-only. Volume ownership labels are checked; this path does not itself verify XFS project assignment. Prove inherited quota coverage and safe handling of drift. |
| Build storage | [Build executor](../../src/Automaton.Runner/Services/HostContainerBuildExecutor.cs) | Build source uses the user-volume deployment staging tree. Inventory caches, artifacts, temporary paths, image layers and logs; bound or account for each. |
| Runtime/build compute | Same managed runtime/build files | Both set the Hosted Runner cgroup parent. The aggregate is keyed by Hosted Runner ID, so multiple Hosted Runners do not automatically share a single account CPU/memory/PID budget. |
| Writable roots | Same managed runtime/build files | Docker argument construction does not set a read-only container root or bounded tmpfs. A read-only artifact mount leaves other container write paths to address. |
| Storage measurement | [Verification result application](../../src/Automaton.Api/Services/HostedRunnerStorageVerificationResultApplier.cs), [reconciliation](../../src/Automaton.Worker/Services/HostedRunnerStorageReconciliationService.cs) | Authoritative XFS usage is verified and persisted per Hosted Runner. Account reporting must identify the actual filesystem/project domain and avoid counting shared project totals more than once. |
| Durable infrastructure commands | [Queue consumer](../../src/Automaton.Runner/Services/InfrastructureCommandQueueConsumer.cs), [callback service](../../src/Automaton.Api/Services/InfrastructureCommandCallbackService.cs), [lifecycle reconciler](../../src/Automaton.Api/Services/HostedRunnerLifecycleReconciler.cs) | Durable queues, result-before-ack delivery, command identity checks, recovery and transactional claim/publish mechanisms exist. Prove replay/crash convergence across replicas and host failure. |
| App deployment lifecycle | [Container manager](../../src/Automaton.Runner/Services/RunnerRuntimeContainerManager.cs), [cleanup](../../src/Automaton.Api/Services/AppDeploymentTargetRuntimeCleanupService.cs), [app deletion](../../src/Automaton.Api/Services/AppDeletionService.cs) | Candidate readiness, durable promotion metadata, failed-candidate cleanup, ownership validation and cleanup-pending state exist. Runtime supervision records unexpected exits; define and prove recovery policy, offline cleanup and retained-data behavior across modes. |
| Hosted Runner resizing | [Admin upgrade guide](../../src/Automaton.Api/Documentation/Admin/hosted-runners/upgrade-runner.md) | Active snapshot promotion and identity repair exist. Guide explicitly states that failed reprovision may leave failed state/new placement and does not automatically restore the old container. This needs a defined recoverable transition. |
| Payments | [Stripe service](../../src/Automaton.Api/Services/StripeService.cs), [webhook service](../../src/Automaton.Api/Services/StripeWebhookService.cs), [billing API](../../src/Automaton.Api/Controllers/BillingController.cs) | Checkout, portal and plan changes exist. Webhook handling immediately downgrades on payment failure; payment success only logs. Reviewed handler has no durable event-ID inbox/order reconciliation. Billing status treats presence of a subscription ID as active. Resolve these before paid launch. |
| Pricing surfaces | [Website](../../src/automaton-vue/src/views/PricingView.vue), [Studio](../../src/automaton-studio-vue/src/features/settings/views/PricingView.vue), [production plan guide](../prod/subscription-plans.md) | Website advertises free BYO infrastructure/AI and paid managed hosting later. Studio has a conditional plan catalog. Production guide still describes AI-credit tiers and future managed storage. Reconcile the catalog and remove obsolete promises; historical Free resource numbers are not verified production configuration. |
| Validation and releases | [Server CI](../../azure-pipelines-studio-server.yml), [Runner image pipeline](../../azure-pipelines-runner-docker.yml), [production skill](../../.agents/skills/production-deployment/SKILL.md) | PR configuration builds/tests all C# projects. Production Runner image authority is Azure DevOps with immutable artifact provenance. Configured gates are not evidence that a particular run or production recovery drill passed. |

### Immediate correctness priorities

1. **Choose and enforce the account resource boundary.** Stable project IDs and per-runner cgroups cannot by themselves implement a global account budget.
2. **Close container write escapes and cover platform storage.** Tenant data, shared Docker storage and control-plane uploads need explicit owners and bounds.
3. **Make policy changes durable and data-preserving.** Billing assignment, pending policy, applied snapshot and actual host enforcement must converge visibly.
4. **Harden billing transitions.** Duplicate/out-of-order events, payment recovery and subscription status need an explicit state model.
5. **Make host replacement and deletion truthful about data.** Reprovisioning elsewhere must not be presented as migration unless data transfer or restoration is completed and verified.

## Decisions to settle before implementation expands

These are proposed defaults for the first managed release, subject to product approval. They are not claims about current production configuration.

| Decision | Recommended first-release contract | Why / alternative |
|---|---|---|
| Resource owner | `ApplicationUser.Id` account, matching the existing allocation | Organization billing/ownership would require an explicit ownership migration. |
| Number of active managed runners | One active managed Hosted Runner per account, enforced atomically across normal/admin/recovery paths | Quarantine or explicitly migrate existing duplicates; never silently delete them. If multiple runners are required at launch, implement account reservations and compute sharing/allocation first. |
| Persistent quota domain | One durable account storage location on one XFS filesystem; scheduling respects that locality | The same project ID on another filesystem represents another independent quota. Multi-host storage needs budget partitioning or a storage service with its own enforcement. |
| Compute budget | Hosted Runner control process + all managed apps + builds share the account's one runner slice | Infrastructure Runner stays outside. Reserve enough tenant memory/PIDs for the control process and bound build concurrency. |
| Resource authority | Keep plan → specification mapping, with explicit policy revision and desired/applied state | Avoid two independently editable numeric authorities. Add separate account budgets only when semantics require them. |
| Billable/limited storage | Publish a category ledger for system data, app data, source/artifacts, caches, uploads, logs, images and backups | Classify each as tenant persistent quota, bounded transient memory, separately limited service storage, or bounded platform overhead. No unowned writable category. |
| Upgrade/downgrade | Durable requested → applying → applied/blocked/failed transition; preserve data | Decide billing effective time, proration, grace, over-limit restrictions and retries. A lower disk limit must not delete data automatically. |
| Commercial catalog | Free/BYO and managed tiers defined from actual cost and guarantees | Set prices only after approved resource sizes, utilization assumptions and recovery/service commitments. Existing guide values are not an approved price list. |
| Reliability target | Explicit availability target, recovery time objective, recovery point objective, retention and support ownership | Select measurable targets before closing the recovery milestone; do not imply automatic failover for local-only data. |

## Delivery milestones

### M0 — Freeze the managed resource contract and acceptance inventory

**Next implementation milestone; prerequisite for account-wide guarantees.**

- Record the decisions above as configuration/schema/API contracts.
- Inventory every tenant write path and resource consumer from upload through build, runtime, published frontend and deletion.
- Implement or verify atomic account admission and app-count enforcement, including concurrent API replicas, pending operations, admin overrides, disabled/deleted runners and recovery.
- Record account storage locality with sufficient host/filesystem identity to distinguish quota domains. Preserve existing project IDs.
- Identify existing accounts that violate the chosen contract and define a non-destructive adoption/migration procedure.
- Inventory scheduled-action limits alongside build concurrency; retain the existing subscription capability and cadence validation.

**Exit:** a concurrent double-create cannot double the entitlement; every storage category has an owner, limit and retention rule; existing account conflicts are surfaced explicitly; policy values come from server authority.

### M1 — Complete managed storage enforcement (old Stages 4–7)

**Status: implemented; live acceptance pending.** M1 implementation is merged; production acceptance still requires the reviewed Worker payload correction, runner reprovision, and the disposable Automaton flow.

Reuse the existing XFS provision/reprovision actions and user-volume topology.

- Verify project assignment and inheritance for system/user roots, existing files, new nested files, runtime data mounts, build source, candidate artifacts and retained caches.
- Fail safely when filesystem, project identity, quota enforcement or ownership cannot be established. Define drift detection/reconciliation so label checks alone do not imply quota health.
- Validate writable paths for supported runtime/build fixtures; make roots read-only where compatible and provide explicit bounded scratch/cache/data mounts.
- Bound `/tmp`, shared memory and other transient writes. Account for tmpfs memory inside the tenant memory budget.
- Bound Docker logs, overlay/image storage and shared caches as platform overhead where they cannot belong to an XFS tenant project; add host watermarks and admission behavior.
- Apply separate limits/retention to control-plane source archives, published frontend assets and media where they live outside the managed host quota.
- Implement safe cleanup for failed/cancelled builds, deployment staging and superseded artifacts, retaining only explicitly promised rollback data.
- Treat source data, runtime durable data and disposable output according to the generic-runtime contract.

**Exit:** real XFS tests show combined system + app + build persistent writes hit one limit; root/tmp/cache escape attempts are denied or bounded; existing data survives reprovision and quota exhaustion; host low-disk conditions remain recoverable.

**Dependencies:** M0 storage/account scope. Complete this before advertising an account storage guarantee.

### M2 — Complete compute, admission and capacity (old Stage 8)

- Verify Hosted Runner, runtime and build containers belong to the intended cgroup after create/restart/reprovision and recovery.
- Read back effective CPU, memory and PID settings rather than relying only on successful command execution.
- Exercise CPU saturation, memory pressure/OOM and PID exhaustion across simultaneous workloads; preserve operator access through the Infrastructure Runner.
- Apply bounded build/action concurrency and queue/backpressure policy; prevent a build from consuming unreserved host capacity.
- Make placement reservations account for pending creates/resizes, failures, deletion, host reserves and stale telemetry under multiple workers.
- If M0 allows multiple runners, implement a real account compute allocation model before declaring this milestone complete. Separate host cgroups cannot form a cross-host kernel limit.

**Exit:** combined workloads cannot exceed the promised aggregate; concurrent placements cannot oversubscribe the configured reservation policy; failed operations release reservations safely.

### M3 — Account usage and effective policy visibility (old Stage 9)

- Expose desired policy, applied policy, project/storage location, authoritative usage, sample time, source, verification status and transition state through API and Studio.
- Identify measurements by durable storage domain plus project ID; never sum identical project totals reported by multiple runners.
- Preserve stale, unavailable, verifying and error states. Unknown usage must not become zero.
- Distinguish logical file sizes from allocated quota bytes and specify what is included/excluded.
- Separate account usage, host capacity and platform overhead; expose actionable admin diagnostics.
- Add bounded measurement retention and metrics for verification age, drift, quota failures and reconciliation lag.

**Exit:** displayed limits match host-enforced values; duplicate/out-of-order reports cannot inflate or regress authoritative usage; stale/unavailable samples are visible and cannot authorize unsafe placement or shrinking.

### M4 — Safe subscription and billing transitions (old Stage 10 plus payment correctness)

- Route admin assignment, Stripe changes and specification revisions through one durable policy-change workflow.
- Keep active snapshots until host confirmation. Show pending/blocked/failed transitions and provide idempotent retry/reconciliation.
- Preflight disk shrink against authoritative usage and shared-project participants; preserve existing identity and data.
- Define coordinated quota changes when multiple historical runners share a project; retain conflict guards until a safe transition replaces them.
- Add a durable Stripe event receipt/reconciliation design, duplicate protection, ordering protection and reconciliation against current provider state.
- Make checkout/customer/subscription creation safe under concurrent requests and retries.
- Handle failed payment, recovery, cancellation at period end, grace, suspension, resumption and deleted/replaced subscriptions explicitly.
- Treat billing payment state and resource-application state separately. Presence of a subscription ID is insufficient customer-facing payment status.
- Verify provider update/payment semantics; do not assume an active subscription update means every prorated invoice has been paid.
- Fix catalog identity/actions: compare stable plan IDs and provide an actionable Free/cancel path. Current Studio labels a Free downgrade while its upgrade handler returns for zero-price/default plans.
- Resolve the differing admin/manual-reprovision and webhook-triggered provisioning behavior, then update guides.

**Exit:** provider test-mode scenarios cover duplicate/reordered events, failure → recovery, repeated checkout, delayed callbacks, plan changes, cancellation and over-quota downgrade; no transition silently deletes data or advertises unapplied resources.

### M5 — Finish supported app lifecycle behavior

- Use the matrix below as a finite release checklist across static/frontend, managed web runtime and actions-only apps, plus applicable self-hosted behavior.
- Extend existing staged promotion and metadata recovery; test crashes before creation, after container creation, before/after promotion and during cleanup.
- Define unexpected-exit/restart-loop policy and make runtime status/routing converge after Runner/API restarts.
- Define overlap during replacement: old and candidate containers may share data and consume one budget. Specify supported single-writer/stateful behavior, migration ownership and rollback limits.
- Finish offline deletion/target-change retry or documented blocked state; preserve ownership checks and expose cleanup debt.
- Keep clone/remix source-only, preserve attribution/configuration metadata, and avoid copying runtime data or live Runner assignments.
- Distinguish stopping, undeploying, deleting an app, deleting a target and deleting retained data in API/CLI/Studio.
- Verify public frontend publication, runtime routing, authentication, target isolation and secrets across these transitions.

**Exit:** each supported matrix row passes with durable status and data semantics. CLI success means completed deployment plus required reachability validation, rather than request acceptance.

### M6 — Infrastructure recovery and operational readiness

Can begin evidence collection alongside M1; production completion depends on the finished resource/lifecycle behavior.

- Exercise infrastructure command replay after host-side success but before callback/ack; ensure duplicate/stale commands cannot repeat destructive effects.
- Test API/Worker/RabbitMQ/Runner restarts and partitions with multiple API/Worker instances. Verify leases/claims, retries and reconciliation converge without conflicting operations.
- Define poison-command handling, bounded retries, operator retry/cancel and observable terminal errors.
- Validate host enrollment/revocation, readiness, maintenance, scheduling disable, drain, restart and decommission.
- Make data locality mandatory during host changes. Add verified transfer/restore with ownership/cutover checks, or block relocation and document recovery from backup.
- Define backup coverage for database/control-plane metadata and durable app data, key/config recovery and consistency expectations; perform a restore to a replacement environment and record measured RPO/RTO.
- Add explicit account storage teardown: ensure all workloads/data retention obligations are resolved, remove storage/quota safely, then release allocation. Keep hard-delete protection until teardown succeeds.
- Exercise failed Hosted Runner reprovision and recover the old service or expose an actionable recoverable state; reconcile placement, identity and routes.
- Define monitoring/alerts for queue age, failed operations, stale hosts/usage, storage watermarks, runtime crash loops, backup freshness, and billing/resource drift.
- Validate tenant authorization, brokered host access, path traversal/symlink handling, secret lifetime/redaction, credential rotation and bounded request/upload/log behavior. Build on existing guards and rate limiting.
- Exercise production deployment rollback with immutable artifacts and database compatibility. Runner production images continue through the existing Azure DevOps authority.

**Exit:** documented operator drills restore service and data within approved targets; no unresolved critical failure/security cases; capacity and cleanup debt are observable; a named owner and runbook exist for each alert.

### M7 — Pricing, documentation and release acceptance

Commercial design can run alongside implementation; enabling paid plans waits for M1–M6 acceptance for the promises sold.

- Build a cost model covering host reserves/utilization, CPU/memory, persistent disk, images/caches/logs, backups, traffic, control-plane storage, payment fees and support. Measure representative build/runtime workloads.
- Approve tier prices, resource sizes, app/build/action limits, retention, included features, support commitments and grace/over-limit rules.
- Align configured plans/specifications, provider product/price IDs, API catalog, Studio, public website and Admin guides. Remove stale AI-credit/project-gating claims from the production plan guide.
- Make availability flags and test-user plans explicit; verify hidden plans cannot be purchased through direct API calls.
- Validate actual production configuration read-only before rollout; source code does not establish which plans are live.
- Collect release evidence: commit SHA, passing required CI, image digest, policy revision, configuration version, acceptance results, recovery drill and outstanding limitations.
- Roll out through test accounts and a limited managed cohort; define observable stop/rollback criteria before enabling general paid checkout.

**Exit:** a user can understand, purchase, receive, observe, change and cancel the product as advertised; operational evidence supports every published guarantee.

## App lifecycle acceptance matrix

| Flow | Required result |
|---|---|
| Create/import/clone/remix | Correct owner/source metadata; subscription admission; no copied live state or secrets. |
| Upload/build | Validated bounded inputs; status/logs; cancellation/timeout; quota/OOM failure; temporary cleanup; previous deployed version preserved on failure. |
| Initial deployment | Appropriate frontend/web/actions-only behavior; configured storage; health/readiness; durable completion status; route/access validation. |
| Replace/redeploy | Candidate failure preserves working version where promised; promotion recovers after crash; stateful data and overlapping writers follow documented policy. |
| Stop/start/unexpected exit | Durable desired/observed state; route changes; defined restart policy and loop limits. |
| Runner reconnect/reprovision | Identity/target repair and routes converge; durable app data and resources remain correct. |
| Change target/host | Verified cleanup and data movement/restore or explicit blocked operation; no claim that reprovision alone migrates data. |
| Unpublish/undeploy/delete | Exact scope shown; inaccessible/offline Runner handled safely; retryable cleanup; retained-data policy enforced. |
| Account cancellation/deletion | Billing/resource transition, retention and explicit storage teardown; no orphan workloads or premature allocation release. |
| Restore | Data, identity, policy, runtime and routes restored and checked; recovery objective measured. |

Run relevant rows for managed hosting and self-hosted Runners, and for static/frontend, web-runtime and actions-only contracts. Mark genuinely inapplicable combinations explicitly.

## Old stage mapping

| Historical stage | Current treatment |
|---|---|
| 1–3: foundation/policy/account identity | Retain completed work; audit admission and ownership assumptions in M0. |
| 4: Hosted Runner storage | Core setup exists; M1 closes enforcement/reprovision evidence. |
| 5: app storage | User-volume subpaths exist; M0/M1 complete topology and coverage. |
| 6: runtime writes | M1 closes root/tmp/other write paths. |
| 7: build storage | Source-volume path exists; M1/M2 finish cache/temp/cleanup/concurrency. |
| 8: aggregate compute | Per-Hosted Runner slice and app/build membership exist; M0/M2 finish account scope and proof. |
| 9: usage | Per-runner verification exists; M3 completes account reporting. |
| 10: changes | Snapshots, guards and provisioning exist; M4 completes durable commercial/resource transitions. |
| Broader reliability, pricing and lifecycle | M5–M7 are additional release scope with separate acceptance. |

## Execution order and change discipline

1. Start with **M0** and a narrow adoption/admission change.
2. Complete **M1**, then **M2**; collect M5/M6 baseline failures alongside this work.
3. Complete **M3**, then resource transitions in **M4**.
4. Complete lifecycle/recovery gaps in **M5/M6**, and the provider transition tests in **M4**.
5. Close **M7** and release only the guarantees supported by evidence.

Use focused PRs per contract or failure case. Do not combine storage topology, billing state and container hardening into one unreviewable migration.

For each implementation PR:

- Link the milestone and concrete acceptance cases; record remaining gaps.
- Follow current repository skills and analyzer rules. For C# changes, build affected projects and run the required final `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal`; run relevant tests and the configured all-project CI gate.
- Use Python action tests, shell validation, Docker/XFS/cgroup acceptance and Studio checks where the changed behavior requires them. Unit mocks alone cannot prove host quotas or recovery.
- Validate schema adoption and rollback/roll-forward behavior on representative existing data.
- Update the nearest Admin guide for actual administrator-visible changes; never document a planned control as available.
- Record unavailable checks and evidence location. A milestone becomes complete only after its exit criteria are demonstrated.

This assessment changes documentation only. It does not certify production readiness, enable paid plans, migrate storage, or start deployment.
