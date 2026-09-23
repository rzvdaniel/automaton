# Hosted Runner Infrastructure and Subscription Resource Plan

**Status (23 September 2026):** The host execution foundation has been demonstrated on a real host. The subscription resource audit, centralized policy resolution, and account storage identity through Stage 3 are implemented. Independent plan-owned budgets, complete account admission rules, and an aggregate account-wide writable-disk limit remain future work.

This plan records the operational model and the subscription-to-enforcement work discussed after the Stage 1D host validation. For detailed code paths and current gaps, see the [subscription resource policy audit](subscription-resource-policy-audit.md). The broader build and runtime hardening program is tracked separately in the [runtime resource isolation roadmap](automaton-runtime-resource-isolation-roadmap.md); its stage numbers refer to a different workstream.

## Operational model

```mermaid
flowchart TD
    CP["Automaton control plane"] -->|"durable infrastructure command"| IR["Infrastructure Runner on a trusted host"]
    IR -->|"provision and manage"| HR["Hosted Runner control container"]
    HR -->|"manage tenant workloads"| APP["App runtime containers"]
    HR -->|"run transient work"| BUILD["Build containers"]
```

1. An operator bootstraps the machine once with the Infrastructure Runner installation and enrollment scripts. The enrolled Infrastructure Runner is the trusted executor for host-level operations.
2. When an account needs a Hosted Runner, the control plane resolves the account's effective subscription and requested resource snapshot, selects an eligible host, and sends the infrastructure provisioning request.
3. The Infrastructure Runner creates or reprovisions the Hosted Runner container, its system and user volumes, its XFS project quota on the host filesystem, and its aggregate CPU, memory, and PID cgroup boundary.
4. The Hosted Runner manages the account's app runtime and build containers. Those workloads share its cgroup boundary; account data and brokered build workspaces under its user volume are charged to its XFS project on that filesystem.

The Infrastructure Runner and its operator-owned host storage remain outside the tenant boundary. Untrusted app and build requests must not set host paths, cgroup paths, XFS project IDs, Docker arguments, or quota commands.

## Reliability claim

Stage 1D validated the host execution mechanism on a real host: provisioning and reprovisioning established the expected compute and storage boundaries, over-quota writes were rejected, and storage measurement and control worked end to end. This is a foundation reliable enough to build on. It is not a claim that every app build, deployment, cleanup, abuse, scheduling, recovery, or observability path is production-complete.

## Current subscription-to-enforcement path

```mermaid
flowchart TD
    PLAN["Effective subscription"] --> CAP["Hosted Runner capability"]
    CAP --> SPEC["Selected admin-managed specification"]
    SPEC --> POLICY["Policy resolver"]
    POLICY --> SNAP["Persisted Hosted Runner snapshot"]
    SNAP --> HOST["Host placement and enforcement"]
```

| Concern | Current behavior | Remaining decision or gap |
| --- | --- | --- |
| Eligibility | The effective plan's Hosted Runner capability selects an active specification. Resolution fails for missing, disabled, or invalid configuration. | Define any additional entitlement and admission semantics explicitly. |
| CPU, memory, disk, and PIDs | The centralized resolver obtains values from the selected admin-managed specification. Provisioning persists a snapshot; the Worker and Infrastructure Runner apply the corresponding cgroup and XFS settings. | A separately stored subscription-owned resource budget does not yet exist. Decide how it bounds specification choices without replacing operator-edited values silently. |
| Maximum applications | The selected specification supplies `MaximumApps`; assignment checks it per Hosted Runner. | Decide whether the product limit is per account or per Hosted Runner before changing admission. |
| Maximum Hosted Runners | The effective policy describes one normal runner per account, and provisioning normally reuses an existing runner. | Add an explicit account-level admission constraint if one is a hard entitlement; concurrent creation is not guaranteed by a database uniqueness rule. |
| Disk identity | `AccountStorageAllocation`, keyed by `ApplicationUser.Id`, owns a stable `StorageProjectId`. Hosted Runners carry a compatibility copy. | The same ID on different hosts or filesystems does not combine their usage into one quota. Other writable paths also remain outside the project. |
| Scheduling | Host selection checks eligibility and capacity using runner resource requests. Co-located same-account runners with conflicting disk snapshots are rejected; host actions also refuse to overwrite a shared project quota. | Placement capacity is not an aggregate account usage counter. Improve failure recovery and explainability as the policy matures. |

The previously discussed Free shape of approximately **0.5 CPU, 768 MiB memory, and 2 GiB disk** is product intent, not proof of current live settings. The resource model's code defaults are **0.5 CPU, 384 MiB memory, 512 MiB disk, 256 PIDs, and 3 applications**; existing administrator-edited specifications can differ. Inspect the persisted Free specification before stating or changing production values. The desired application count, particularly for static apps, still needs an explicit policy decision.

## Completed subscription resource stages

### Stage 1 — Trace and audit the resource path

**Complete.** The audit followed effective subscription → capability → specification → snapshot → host scheduling → Worker payload → host enforcement. It identified which limits are plan-selected, which are specification-derived, and which storage paths escape the current XFS project. See the [detailed audit](subscription-resource-policy-audit.md).

### Stage 2 — Centralize effective policy resolution

**Complete.** `SubscriptionResourcePolicyResolver` supplies CPU, memory, disk, tasks/PIDs, maximum Hosted Runners, and maximum applications through one control-plane resolution contract. Values still come from the selected administrator-managed specification. Normal provisioning does not accept client-provided resource limits. This stage did not add an independent plan budget or change product values.

### Stage 3 — Give the account a durable storage identity

**Complete in [PR #300](https://github.com/rzvdaniel/automaton/pull/300).** `AccountStorageAllocation` owns one stable positive XFS project ID per account. The migration adopts one unambiguous historical Hosted Runner ID and fails on conflicts; provisioning and reprovisioning reuse the allocation, and runner deletion does not release it. `HostedRunner.StorageProjectId` remains a synchronized compatibility snapshot for the existing Worker and reconciliation contracts.

Multiple Hosted Runners for an account may carry the same project ID. Placement and Infrastructure Runner guards prevent conflicting disk-limit snapshots from overwriting a shared XFS quota on the same host filesystem. User hard deletion is blocked while the allocation exists; no storage teardown action has yet been designed. Stage 3 did not add a global quota counter, migrate other writable paths, or change the app/build container storage contract.

## Next work, subject to product decisions

1. **Define authoritative account policy.** Decide whether CPU, memory, disk, PIDs, maximum Hosted Runners, and maximum applications are account entitlements or runner-level shapes. Preserve existing operator-edited specification values during a transition. Keep normal clients unable to choose enforcement values.
2. **Enforce admission consistently.** Apply the chosen limits at every creation, assignment, and reprovisioning path, including concurrent requests and retries. Record clear rejection reasons. A displayed policy value alone is insufficient.
3. **Close writable-disk escapes.** Inventory account-owned persistent, build, cache, and temporary writes. Keep infrastructure-owned images, logs, and host state outside the tenant budget. Use explicit quota-backed writable paths and bounded temporary storage; make app/build root filesystems read-only where compatible.
4. **Make `DiskLimitMb` an actual account budget.** Define how usage is measured and enforced across different hosts and filesystems, or explicitly constrain placement and storage to an enforcement domain until trusted aggregate accounting exists. A shared numeric XFS ID by itself does not aggregate usage across filesystems.
5. **Prove operations and recovery.** Test exhaustion, quota increase/decrease, concurrent provisioning, host loss, reprovisioning, stale measurements, and cleanup. Expose trusted usage and failure reasons in API and administration surfaces before treating proposed Free values as an enforced product contract.

Do not introduce paid tiers, organization policy, separate per-app CPU budgets, or build-concurrency fields solely to complete this plan. Those require their own product decisions.
