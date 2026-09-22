# Subscription Resource Policy Audit

**Status:** Stage 1 audit plus Stage 2 policy-resolution contract. The policy
resolver is now implemented; account-wide disk ownership/migration and resource
value changes remain future work.

## Scope and conclusion

The subscription model currently controls feature eligibility and selection of a Hosted Runner specification. It does not persist an independent account resource budget. `SubscriptionResourcePolicyResolver` now provides one authoritative control-plane resolution path, deriving CPU, memory, disk, PID/tasks, and maximum applications from the selected administrator-managed specification. Provisioning snapshots those effective values onto each `HostedRunner`, then carries them through the Worker and Infrastructure Runner to Docker/cgroup and XFS enforcement. Other Hosted Runner-specific settings (memory reservation, shared memory, Docker log rotation) remain sourced from the same specification and are preserved in the snapshot.

The persisted `DiskLimitMb` is a per-Hosted-Runner limit. The current project quota aggregates the Hosted Runner's system and user Docker volumes under one XFS project. It is not account-wide across multiple Hosted Runners, Docker writable layers, image/cache data, or any storage outside those two Docker volume roots. Infrastructure Runner host resources and storage are operator-owned and remain outside tenant accounting.

## Current resource flow

```mermaid
flowchart TD
    U[ApplicationUser.SubscriptionPlanId] --> P[SubscriptionPlan.CapabilitiesJson]
    P --> C[HostedRunner enabled + specification id]
    C --> S[Admin-managed HostedRunnerSpecification.Resources]
    S --> PR[SubscriptionResourcePolicyResolver]
    PR --> R[HostedRunnerProvisioningService]
    R -->|copy resource values| HS[HostedRunner.SpecificationSnapshot / PendingSpecificationSnapshot]
    R -->|allocate per-runner id| SP[HostedRunner.StorageProjectId]
    HS --> W[Worker ManagedRunnerProvisionRequestedEventHandler]
    SP --> W
    W -->|InfrastructureCommandRequestedEvent JSON| IR[Infrastructure Runner]
    IR -->|Docker run + cgroup parent| CG[Hosted Runner cgroup: CPU, memory, PIDs]
    IR -->|system + user Docker volumes, one project| XFS[XFS project quota: diskLimitMb]
    CG --> APP[Application and build containers share runner cgroup]
    XFS --> VOL[Runner system volume + user volume]
```

`HostedRunnerSpecificationResolver` follows the subscription capability to the selected specification. `SubscriptionResourcePolicyResolver` validates and maps the policy-owned resource values from that specification. During provisioning, the selected specification is copied into the HostedRunner snapshot, then those values are replaced with the resolved policy values; specification-only settings are retained. Provisioning payloads use the pending snapshot when present, otherwise the persisted snapshot. Container-level app/build commands use the Hosted Runner cgroup and the Hosted Runner user volume.

## Resource authority table

| Resource | Current source of truth | Persisted where | Enforced where | Intended future source of truth |
| --- | --- | --- | --- | --- |
| Hosted Runner CPU | Effective `SubscriptionResourcePolicy.CpuLimitCores`, resolved from admin-managed `HostedRunnerSpecification.Resources.CpuLimit`; code model default is 0.50 | Specification and revision; copied into `HostedRunner.SpecificationSnapshot` (and pending snapshot for reprovision) | Infrastructure action creates runner cgroup with CPU quota; hosted Runner, app, and build containers join that parent cgroup | A subscription-owned resource policy independent of the Hosted Runner specification |
| Hosted Runner memory | Effective `SubscriptionResourcePolicy.MemoryLimitMb`, resolved from admin-managed specification; model default 384 MiB | Specification/revision and runner snapshot | Runner cgroup memory limit; Docker memory reservation is also sent for the Hosted Runner container | A subscription-owned resource policy independent of the Hosted Runner specification |
| Hosted Runner disk | Effective `SubscriptionResourcePolicy.DiskLimitMb`, resolved from admin-managed specification; model default 512 MiB | Specification/revision; runner snapshot; `HostedRunner.StorageAllowanceBytes` derived from the effective snapshot | One XFS project hard quota over the Hosted Runner system and user Docker volume roots | A subscription-owned `DiskLimitMb`, enforced once across every tenant-owned writable path for that account |
| Hosted Runner Tasks/PIDs | Effective `SubscriptionResourcePolicy.TasksLimit`, resolved from admin-managed `PidsLimit`; model default 256 | Specification/revision and runner snapshot (`PidsLimit`) | Hosted Runner cgroup `pids.max`; app/build containers are descendants under the same runner cgroup | A subscription-owned task/PID limit independent of the Hosted Runner specification |
| Max Hosted Runners | `SubscriptionResourcePolicy.MaxHostedRunners` currently describes the existing normal lifecycle as one per user; no new admission check is added, and no database uniqueness constraint guarantees it under concurrent creation | No separate persisted policy; HostedRunner rows and their status/deletion state | Existing API provisioning lookup/lifecycle reuses an existing non-deleted/non-disabled runner; explicit cap enforcement remains future work | A subscription-owned `MaxHostedRunners` with separately defined enforcement semantics |
| Max applications | Effective `SubscriptionResourcePolicy.MaxApplications`, resolved from specification `MaximumApps` (model default 3); currently remains a per-HostedRunner count | Specification/revision and HostedRunner snapshot | `HostedRunnerAppCapacityService` checks deployment target assignments per HostedRunner during assignment | A subscription-owned `MaxApplications`; account-wide semantics still require product definition |
| Build concurrency | No subscription or Hosted Runner build concurrency limit found. Build commands have a per-build timeout; deployment operation/lease logic coordinates the same operation but is not a plan concurrency quota. Concurrent distinct builds can contend within the runner's aggregate cgroup/resources. | Deployment/operation state, not a resource-policy value | Runner execution timeout and shared cgroup; no policy-level admission limit identified | Keep out of the minimal policy until a product requirement and admission model exist |
| App container CPU/memory/PIDs | No app-specific resource values in the app deployment contract or Docker create request | No app resource snapshot found | App container is placed under its Hosted Runner's cgroup parent, so it shares the aggregate runner limit; no per-app Docker CPU/memory/PID limits are set | Account policy as aggregate cap; per-app subdivision is a later, separate feature if needed |
| App persistent data and declared storage mounts | App/application storage declarations select data subpaths; no app/account quota value | Hosted Runner user Docker volume under `workspace/data/{appId}/...` | Included in the Hosted Runner's shared XFS project only because it resides in that runner's user volume | Account `DiskLimitMb`, across apps, Hosted Runners, and other account-writable storage |
| Build workspace | Deployment source/package and Runner workspace layout; no disk allowance of its own | Hosted Runner user volume for brokered Hosted Runner builds, under `workspace/runner/deployments/...` | Included in that runner's user volume XFS project | Account `DiskLimitMb` |
| Build cache | No account resource policy. Application build/runtime cache directories may be created in the build workspace; Runner also has a `CacheRoot` workspace concept. No distinct capped or centrally accounted build cache was found in the Hosted Runner broker path. | Build output/cache that lands in the deployment source workspace is in the user volume; other Runner cache paths depend on runtime/workspace placement | User-volume quota only for writes under that volume. No separate cache quota | Account `DiskLimitMb` when disk-backed and tenant-owned |
| `/tmp` in app/build containers | Image/runtime defaults; no explicit tmpfs setting found in managed Docker app or build create/run arguments | Container writable layer unless image/runtime redirects it | No explicit tmpfs bound and no XFS project quota on the container writable layer | Bounded tmpfs (memory-backed and subject to account memory) |
| Hosted Runner `/tmp` | Runner image/container default; no tmpfs or size option in provision action | Hosted Runner container writable layer | Runner aggregate memory/PID/CPU cgroup, but not Hosted Runner XFS volume project quota | Bounded tmpfs (memory-backed); no persistent writable escape path |
| Writable root filesystem | Docker defaults to a writable container layer for Hosted Runner, apps, and builds; no `--read-only` or equivalent found on these paths | Docker storage driver/host Docker root, outside the two quota-assigned volume trees | Not covered by Hosted Runner XFS project quota. App/build cgroup placement limits CPU/memory/PIDs, not writable layer bytes | Read-only app/build root with explicit writable tmpfs and quota-accounted data/workspace mounts; bounded Hosted Runner mutable paths |
| Infrastructure Runner | Operator provisioning/configuration and host capacity; not selected as tenant policy | Infrastructure host/container/operator state | Infrastructure host/container boundary; it executes trusted host provisioning and is not charged to tenant XFS project | Remains outside tenant/account resource policy |

### Current effective resource values

The current policy resolver does not change product values: it returns the selected specification's current CPU, memory, disk, PID, and `MaximumApps` values. Repository code defaults are **0.5 CPU, 384 MiB memory, 512 MiB disk, 256 PIDs, and 3 applications**. Seed code creates the Free Hosted Runner specification with `new HostedRunnerResources()` only when that specification is absent. It does not overwrite an existing persisted/operator-edited specification. Production values can differ; inspect the current production specification for exact live values.

### Future product values

Previously discussed target values (0.5 CPU, 768 MiB memory, 2048 MiB disk, 128 PIDs) are future product intent only. They are not made authoritative by this stage and are not substituted by the resolver.

## Implemented Stage 2 policy contract

`src/Automaton.Api/Models/SubscriptionResourcePolicy.cs` defines the small effective policy value. `src/Automaton.Api/Services/SubscriptionResourcePolicyResolver.cs` resolves it through the effective subscription capability and selected active Hosted Runner specification. The values are currently backed by `HostedRunnerSpecification.Resources`; this is a centralized resolution contract, not a separately stored account entitlement or new plan model.

The policy exposes `CpuLimitCores`, `MemoryLimitMb`, `DiskLimitMb`, `TasksLimit`, `MaxHostedRunners`, and `MaxApplications`. `MaxApplications` maps directly from the selected specification's `MaximumApps`. `MaxHostedRunners` is set to 1 to describe the current normal reuse lifecycle; Stage 2 does not add account admission enforcement or a database uniqueness guarantee. Normal provisioning request DTOs contain no resource override properties. The administrator-only Hosted Runner specification APIs remain the trusted way to change backing configuration.

The resolver fails explicitly when the effective plan is missing/inactive, capability is disabled or incomplete, the selected specification is missing/inactive, or policy-owned resource values fall outside the existing supported ranges. No unlimited or hard-coded resource fallback is introduced.

## Assignments and propagation of limit values

| Value | Assignment / propagation path |
| --- | --- |
| `CpuLimit` | `HostedRunnerResources` defaults to 0.50; admin specification create/update persists operator-supplied values; Free seed creates defaults; `HostedRunnerSpecificationSnapshotFactory` copies the specification to a runner snapshot; provisioning chooses the specification; Worker serializes snapshot value as `cpuLimit`; `provision-hosted-runner` and `reprovision-hosted-runner` validate it and use it when ensuring the resource cgroup. |
| `MemoryLimitMb` | Same specification/default/snapshot path; Worker serializes `memoryMb`; Infrastructure action sets the hosted-runner cgroup memory ceiling and starts the Hosted Runner container under that cgroup. |
| `DiskLimitMb` | Same specification/default/snapshot path; API sets `StorageAllowanceBytes = DiskLimitMb * MiB`; Worker serializes `diskLimitMb`; Infrastructure action applies one project hard block quota to the two Hosted Runner volume roots. Host scheduling also reserves this per-runner estimate; it is not aggregate tenant accounting. |
| `PidsLimit` / Tasks | Same specification/default/snapshot path; Worker serializes `pidsLimit`; Infrastructure action writes the runner cgroup PID ceiling. App and build containers inherit the shared cgroup tree. |
| `MaximumApps` | Specification default/admin value copied to the snapshot. `HostedRunnerAppCapacityService` reads the persisted runner snapshot during runner assignment. |

Relevant implementations: `src/Automaton.Infrastructure/Models/HostedRunnerSpecifications.cs`, `src/Automaton.Api/Extensions/DataExtensions.cs`, `src/Automaton.Api/Services/HostedRunnerSpecificationResolver.cs`, `src/Automaton.Api/Services/HostedRunnerProvisioningService.cs`, `src/Automaton.Worker/Handlers/ManagedRunnerProvisionRequestedEventHandler.cs`, and `system-apps/automaton-infrastructure/actions/{provision-hosted-runner,reprovision-hosted-runner}/main.py`.

## `StorageProjectId` ownership assumptions

Today this is explicitly a **HostedRunner-owned XFS project identity**, not an account identity:

1. `HostedRunner.StorageProjectId` is a nullable field on `HostedRunner`, with a unique database index.
2. `HostedRunnerProvisioningService.AllocateStorageProjectIdAsync(hostedRunnerId)` chooses an unused value by hashing the HostedRunner ID and probing all other HostedRunner rows. It does not use an account ID or account disk-budget record.
3. `ManagedRunnerProvisionRequestedEventHandler` requires that HostedRunner field and serializes it as `xfsProjectId` alongside `hostedRunnerId`, `systemVolumeName`, and `userVolumeName`.
4. The Infrastructure Runner action validates both volume names against the HostedRunner ID, then assigns the same project ID to those two volume roots and sets one hard quota for the project.
5. `HostedRunnerStorageReconciliationService` reads the `StorageProjectId` through the verification's HostedRunner and dispatches the same HostedRunner-scoped volume names and project ID for measurement.
6. The telemetry fallback derives the storage allowance from the HostedRunner snapshot's `DiskLimitMb`.
7. `HostedRunnerManagementService` derives system/user volume names from the HostedRunner ID for deletion actions.
8. The uniqueness index is only among HostedRunner values; it does not reserve an account project shared across multiple HostedRunners or other tenant storage roots.

This ownership shape means the current quota cannot implement the intended one-budget-per-account model. Future account-level accounting will need a durable account-level quota identity and a controlled way to associate all account-owned writable roots/paths with it; the migration is explicitly outside this audit stage.

## Writable tenant disk paths found

### Paths currently inside the Hosted Runner XFS project

- Hosted Runner system Docker volume: provisioned as `automaton-hosted-runner-{id}-system`, mounted at `/automaton/system`. Includes writable Runner system state/configuration that is actually placed there.
- Hosted Runner user Docker volume: `automaton-hosted-runner-{id}-user`, mounted at `/automaton/storage`.
- Runner workspace below the user volume: `workspace/apps/...` for runtime candidate artifacts; `workspace/data/{appId}/...` for app data and declared storage mounts; `workspace/runner/deployments/...` for deployment staging/source/build workspaces; and other user workspace content written under the configured workspace root.
- Brokered hosted app containers mount accepted application artifacts read-only at `/automaton/app`, and data/mount subpaths from the user volume read-write at `/automaton/data` and `/automaton/data/{mount}`.
- Brokered build containers mount the incoming deployment source subpath in the same user volume read-write at `/automaton/source`.

The XFS quota counts all blocks charged to those two volume roots under the same project ID, including user storage and app/build workspaces within the user volume. This is aggregate across these two volumes for one HostedRunner only.

### Tenant-writable paths currently outside that XFS project

- The Hosted Runner container writable layer, including its default `/tmp`, any path not on `/automaton/system` or `/automaton/storage`, and writes to image/container overlay state.
- Hosted app container writable layers, including app `/tmp`, despite `/automaton/app` being read-only and `/automaton/data` being mounted separately.
- Build container writable layers and `/tmp`; only the source workspace is mounted from the quota-assigned user volume.
- Any Runner workspace/cache directory whose configured root is not the brokered Hosted Runner user volume. The direct/local Runner Docker path uses host bind mounts (`DockerContainerRuntime`) and is not the same enforcement path as Hosted Runner brokered execution.
- Docker image layers, build/pulled image cache, Docker logs, container metadata, and other Docker host storage live in the Infrastructure Runner host Docker root. These are infrastructure-owned shared host resources and do not consume the tenant XFS project. Per-runner Docker local log rotation is configured, but logs are not part of the tenant quota.

### Disk-accounting escape paths / limitations

- Docker writable layers are not under either project-tagged Hosted Runner volume.
- `/tmp` is not explicitly mounted as tmpfs, has no configured size bound, and is not under the project quota.
- Build dependency/cache data can be in the source workspace (quota-covered) or image/container writable layer (not quota-covered); there is no single explicit build-cache root/quota.
- Multiple Hosted Runners for one account would each get their own project and full per-runner `DiskLimitMb`; the current model does not sum tenant storage across them.
- App data and app declared volumes are quota-covered only when they are routed through the Hosted Runner's brokered user volume. Other/local/self-hosted execution storage is outside this Hosted Runner quota by design.
- Host scheduling checks estimated per-runner disk reservations against host-reported capacity. That is placement admission, not account usage accounting or quota enforcement.

Infrastructure Runner's own system, image cache, command workspace, and host storage are deliberately not charged to the tenant quota. Keep that distinction when adding tenant-wide accounting.

## Current `/tmp` and root filesystem behavior

No explicit `--tmpfs /tmp`, size limit, or equivalent is configured for the Hosted Runner, application, or build container creation paths found in the repository. Docker therefore supplies its normal writable container root layer (including `/tmp` unless the image changes it). `/tmp` writes can consume host Docker storage outside the HostedRunner-owned XFS project. CPU/memory/PID cgroup limits do not provide a disk byte cap.

The Hosted Runner's `/automaton/system` and `/automaton/storage` are persistent named Docker volume mounts. Its other writable paths remain in the container layer. App artifacts at `/automaton/app` are mounted read-only, which protects the accepted artifact tree, but the app root filesystem as a whole is not read-only: the container receives no read-only root setting and retains writable-layer paths. `/automaton/data` and declared storage paths are explicit writable mounts inside the quota-assigned Hosted Runner user volume on the brokered path. Build source is writable and quota-covered on that path, but build rootfs and tmp remain writable outside it.

## Docker volume behavior for app and build execution

- **Brokered Hosted Runner apps:** `HostContainerExecutionService.CreateApplicationContainerAsync` verifies the HostedRunner-labeled user volume, mounts candidate artifact subpath read-only, mounts app data and declared storage subpaths read-write, and places the container in the HostedRunner cgroup. No per-app CPU/memory/PID limit, read-only rootfs, or tmpfs is added.
- **Brokered Hosted Runner builds:** `HostContainerExecutionService.RunBuildAsync` mounts the deployment attempt source subpath from the HostedRunner user volume. It uses the HostedRunner cgroup, but does not mount a dedicated cache or tmpfs and does not make rootfs read-only.
- **Direct/local Runner Docker runtime:** `DockerContainerRuntime` uses host bind mounts for build source, app artifacts (read-only), and app data/storage (read-write). It does not set account policy limits; it is not the Infrastructure Runner brokered Hosted Runner path.
- **Hosted Runner system/user volumes:** provision and reprovision actions create/retain both named volumes, attach the same XFS project, and verify one `diskLimitMb` hard block quota for the project. Reconciliation measures those expected volumes by HostedRunner-scoped identity.

## Future independent subscription policy

Keep the contract limited to the requested account policy values and avoid product-tier or organization-specific abstractions for now:

```csharp
public sealed record SubscriptionResourcePolicy
{
    public decimal CpuLimitCores { get; init; }
    public int MemoryLimitMb { get; init; }
    public int DiskLimitMb { get; init; }
    public int TasksLimit { get; init; }
    public int MaxHostedRunners { get; init; }
    public int MaxApplications { get; init; }
}
```

The contract is currently resolved from the effective account subscription through the existing specification. A future transition may store policy values independently; the policy should then become the authoritative ceiling while the Hosted Runner specification describes an operator-approved runtime shape. Persisted provisioning snapshots should continue to capture effective limits for deterministic operation history.

`DiskLimitMb` means one account-level aggregate writable-disk budget. `StorageProjectId` should evolve from a HostedRunner-owned identity to an account-owned quota identity (or equivalent shared ownership mapping) so all account roots are charged to the same quota. Infrastructure Runner host state, image cache, and host Docker storage remain outside that tenant budget. Keep `/tmp` bounded and memory-backed. Do not add separate build concurrency, per-app CPU, paid tiers, or organization policy fields without a corresponding product requirement.

## Recommended migration sequence

1. **Independent policy source:** move effective limits into subscription-owned configuration; define whether `MaxApplications` is per account. Preserve current operator-edited values during any conversion.
2. **Policy ceilings and admission:** decide how specification shape interacts with subscription limits, then add account-level runner/app admission where explicitly intended. Current Stage 2 only centralizes resolution.
3. **Account storage ownership design:** add account-level quota identity and mapping before moving any existing data. Inventory roots, handle XFS project ownership safely, and plan rollback/reconciliation. Do not migrate storage until that design is reviewed.
4. **Close disk escapes:** stage mount and image/log accounting decisions; make app/build rootfs read-only where compatible; provide explicit writable quota-backed paths and bounded tmpfs `/tmp`; ensure build temporary output/cache lands in accounted workspace or bounded tmpfs. Preserve infrastructure-owned storage outside tenant accounting.
5. **Enforcement and observability:** apply aggregate account quota across all account paths, report account usage from trusted host measurements, and test exhaustion and provisioning/reprovision recovery before changing production policy values.

This sequence is a recommendation only; this task does not begin Stage 2 or implement migrations.

## Principal code references

- `src/Automaton.Infrastructure/Models/PlanCapabilities.cs` — plan capabilities currently include HostedRunner enablement/specification mapping and scheduled-action limits, but no resource budget.
- `src/Automaton.Infrastructure/Models/HostedRunnerSpecifications.cs` — resource code defaults and specification snapshot copying.
- `src/Automaton.Infrastructure/Entities/SubscriptionPlan.cs`, `src/Automaton.Infrastructure/Entities/ApplicationUser.cs` — plan storage and user-to-plan association.
- `src/Automaton.Api/Extensions/DataExtensions.cs` — Free plan/specification seed defaults.
- `src/Automaton.Api/Services/HostedRunnerSpecificationResolver.cs` — plan-to-specification resolution.
- `src/Automaton.Api/Models/SubscriptionResourcePolicy.cs` and `src/Automaton.Api/Services/SubscriptionResourcePolicyResolver.cs` — Stage 2 effective policy contract and centralized resolver.
- `src/Automaton.Api/Services/HostedRunnerProvisioningService.cs` — snapshot persistence, storage allowance/project allocation, host placement reservations.
- `src/Automaton.Worker/Handlers/ManagedRunnerProvisionRequestedEventHandler.cs` — provisioning payload from persisted snapshot and HostedRunner `StorageProjectId`.
- `system-apps/automaton-infrastructure/actions/provision-hosted-runner/main.py` and `reprovision-hosted-runner/main.py` — host cgroup and XFS project setup; volume mount list; Docker runner launch.
- `src/Automaton.Runner/Services/HostContainerExecutionService.Runtime.cs` and `HostContainerExecutionService.cs` — brokered app/build container mounts and cgroup placement.
- `src/Automaton.Runner/Services/DockerContainerRuntime.cs` — direct/local Docker bind mounts and absence of corresponding resource policies.
- `src/Automaton.Worker/Services/HostedRunnerStorageReconciliationService.cs`, `src/Automaton.Api/Services/RunnerStorageTelemetryService.cs` — HostedRunner-scoped measurement/allowance handling.
