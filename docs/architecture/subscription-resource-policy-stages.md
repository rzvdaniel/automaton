# Subscription resource policy plan

## Current tracking status

- Stages 1–3 are complete.
- Stage 4 is next; Stages 5–10 remain planned.
- Keep implementation object-oriented with small cohesive classes and methods, and control cyclomatic complexity. Split policy resolution, orchestration, storage preparation, and verification into clear responsibilities instead of large functions.

## Original staged prompts (verbatim)

The supplied prompts below are preserved without rewriting their requirements.

> Yes. Below are the stages as separate copy-paste prompts so you can run them one by one in Codex.
>
> Stage 1 — Audit current subscription/resource policy
> We are starting a new resource-policy phase for Automaton.
>
> Context
> =======
>
> Stage 1D has passed on the real host.
>
> We have proven:
>
> - Infrastructure Runner provisioning works.
> - Hosted Runner provisioning/reprovisioning works.
> - Hosted Runner aggregate cgroup limits work.
> - XFS project quota works.
> - Storage measurement works.
> - Actual quota enforcement works.
> - Infrastructure Runner remains outside the tenant resource boundary.
>
> Current Free-plan intent is approximately:
>
> CPU:
> 0.5 core
>
> Memory:
> 768 MiB
>
> Disk:
> 2048 MiB
>
> Tasks/PIDs:
> 128
>
> Max apps:
> currently generous / existing Free policy
>
> Important architectural intent
> ===============================
>
> Resource limits should ultimately come from the subscription/account policy.
>
> Disk allowance must be AGGREGATE across all writable disk owned by the account.
>
> If Free has 2 GiB disk, that must NOT mean:
>
> 2 GiB Hosted Runner
> + 2 GiB App A
> + 2 GiB App B
> + 2 GiB each volume
>
> It must mean ONE 2 GiB account-level writable disk budget.
>
> Eventually this aggregate should include:
>
> - Hosted Runner persistent system storage
> - Hosted Runner user storage
> - app persistent volumes
> - app file/data storage
> - build workspaces
> - build cache if disk-backed
> - disk-backed temporary workspace
>
> Infrastructure Runner storage is infrastructure-owned and must remain outside the
> tenant/account quota.
>
> For /tmp, intended direction is:
>
> - bounded tmpfs
> - therefore memory-backed rather than persistent disk-backed
> - no unlimited writable disk escape hatch
>
> Do NOT implement storage migration yet in this stage.
>
> PROCESS
> =======
>
> We want focused PRs merged to main as soon as they are reviewed and tested.
>
> Do not create a large multi-stage branch.
>
> This stage is AUDIT + CONTRACT ONLY.
>
> OBJECTIVE
> =========
>
> Trace the current complete path from:
>
> Subscription/account
> → resource/default policy
> → Hosted Runner specification
> → Worker/provisioning payload
> → Infrastructure Runner
> → actual host enforcement
>
> Determine what is authoritative today and what is only a default.
>
> Audit:
>
> - CPU
> - memory
> - disk
> - TasksMax/PIDs
> - max Hosted Runners
> - max applications
> - build concurrency if present
> - app container limits
> - Hosted Runner storage
> - app volumes
> - build workspace/cache
> - /tmp
> - writable root filesystem
>
> For every value determine whether it currently comes from:
>
> - subscription
> - plan
> - hard-coded default
> - API request
> - persisted HostedRunner value
> - application value
> - admin/operator input
> - other source
>
> Also identify all places where:
>
> StorageProjectId
>
> is currently assumed to belong specifically to HostedRunner.
>
> DELIVERABLE
> ===========
>
> Create an architecture/audit document in the repository.
>
> Suggested path:
>
> docs/architecture/subscription-resource-policy-audit.md
>
> It should contain:
>
> 1. Current resource flow diagram.
> 2. Table:
>    Resource
>    Current source of truth
>    Persisted where
>    Enforced where
>    Intended future source of truth
> 3. All code paths that assign:
>    CpuLimit
>    MemoryLimit
>    DiskLimitMb
>    Tasks/PIDs
> 4. All StorageProjectId ownership assumptions.
> 5. All writable tenant disk paths.
> 6. All known disk-accounting escape paths.
> 7. Current /tmp behavior.
> 8. Current app root filesystem write behavior.
> 9. Current app/build Docker volume behavior.
> 10. Recommended minimal SubscriptionResourcePolicy contract.
>
> Recommended target contract should be approximately:
>
> SubscriptionResourcePolicy
> {
>     CpuLimitCores
>     MemoryLimitMb
>     DiskLimitMb
>     TasksLimit
>     MaxHostedRunners
>     MaxApplications
> }
>
> Do not over-generalize.
>
> Do not implement pricing/Pro/Organizations yet.
>
> TESTING
> =======
>
> Add architecture/unit tests only if useful to lock down existing invariants.
>
> No production behavior changes should be made in this stage unless required to make
> the audit mechanically accurate.
>
> VALIDATION
> ==========
>
> Run relevant tests/builds and:
>
> git diff --check
>
> FINAL RESULT
> ============
>
> Report:
>
> 1. current resource source-of-truth findings
> 2. current subscription integration status
> 3. current StorageProjectId ownership model
> 4. all writable disk paths found
> 5. current /tmp behavior
> 6. current rootfs behavior
> 7. recommended SubscriptionResourcePolicy shape
> 8. recommended migration sequence
> 9. changed files
> 10. validation
> 11. branch
> 12. commit
> 13. PR number
>
> Open a focused PR.
>
> Do NOT start Stage 2 in this task.
> Stage 2 — Introduce authoritative subscription resource policy
> Continue the subscription resource-policy work from merged Stage 1.
>
> IMPORTANT PROCESS
> =================
>
> Start from current canonical main.
>
> Stage 1 audit must already be merged.
>
> Create one focused branch and PR.
>
> Merge promptly once reviewed and green.
>
> Do NOT implement account-level XFS migration yet.
>
> OBJECTIVE
> =========
>
> Introduce one authoritative control-plane contract that resolves effective resource
> limits for an account/subscription.
>
> Target concept:
>
> SubscriptionResourcePolicy
> {
>     CpuLimitCores
>     MemoryLimitMb
>     DiskLimitMb
>     TasksLimit
>     MaxHostedRunners
>     MaxApplications
> }
>
> For the current Free plan, use the existing intended policy values, approximately:
>
> CPU:
> 0.5 core
>
> Memory:
> 768 MiB
>
> Disk:
> 2048 MiB
>
> Tasks:
> 128
>
> Max apps:
> preserve the current intended Free behavior
>
> The exact values should come from existing product/domain policy where already defined.
> Do not duplicate constants unnecessarily.
>
> ARCHITECTURAL RULES
> ===================
>
> 1. Clients must not be authoritative for these limits.
>
> 2. Hosted Runner creation/provisioning must derive effective values from the
>    account/subscription resource policy.
>
> 3. Persisted resource values may remain on HostedRunner for:
>    - auditability
>    - deterministic reprovision
>    - historical state
>
>    but their origin must be policy, not arbitrary client input.
>
> 4. Admin/internal overrides, if they exist, must remain explicit and trusted.
>
> 5. Do not implement Pro/paid tiers yet.
>
> 6. Do not redesign subscription architecture more broadly than required.
>
> IMPLEMENTATION
> ==============
>
> Introduce a focused service/value object such as:
>
> ISubscriptionResourcePolicyResolver
>
> or equivalent existing naming/style.
>
> It should resolve policy from the account/subscription context.
>
> Wire it into Hosted Runner creation / effective specification creation.
>
> Ensure:
>
> Free subscription/account
> → Free resource policy
> → Hosted Runner persisted effective limits
> → existing provisioning path
>
> Do not change low-level XFS/cgroup enforcement.
>
> TESTS
> =====
>
> Add focused tests proving:
>
> 1. Free account resolves expected CPU.
> 2. Free account resolves expected memory.
> 3. Free account resolves expected disk.
> 4. Free account resolves expected TasksLimit.
> 5. Hosted Runner creation uses resolved policy.
> 6. Client input cannot silently override authoritative resource limits.
> 7. Reprovision continues to use persisted effective specification.
> 8. Missing/invalid subscription policy fails explicitly rather than silently
>    granting unlimited resources.
> 9. Existing Free behavior is preserved where intended.
>
> VALIDATION
> ==========
>
> Run:
>
> - subscription/account tests
> - Hosted Runner creation tests
> - Worker provisioning tests
> - API focused tests
> - architecture tests
> - affected Release builds
> - git diff --check
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. policy resolver location
> 2. Free values
> 3. resource derivation flow
> 4. client override behavior
> 5. persisted effective-limit behavior
> 6. tests
> 7. validation
> 8. changed files
> 9. branch
> 10. commit
> 11. PR number
>
> Do NOT implement account-level StorageProjectId yet.
> Stage 3 — Move storage identity to account scope
> Continue from merged subscription resource-policy work.
>
> Start from current canonical main.
>
> OBJECTIVE
> =========
>
> Introduce account/subscription-level storage identity so that disk quota can become
> one aggregate quota across all account-owned writable storage.
>
> Current historical model
> ========================
>
> StorageProjectId has been associated with HostedRunner.
>
> Stage 1D proved that XFS project quota enforcement works.
>
> Target model
> ============
>
> StorageProjectId belongs to the account/subscription resource boundary.
>
> Conceptually:
>
> Account
>     StorageProjectId
>     DiskLimitMb
>
> Hosted Runner
> Apps
> Volumes
> Build storage
>
> all use the SAME account StorageProjectId.
>
> IMPORTANT
> =========
>
> Do NOT yet migrate every app/build volume in this stage.
>
> This stage establishes:
>
> - account ownership
> - persistence
> - allocation
> - lifecycle
> - migration compatibility
>
> DESIGN REQUIREMENTS
> ===================
>
> The account-level StorageProjectId must be:
>
> - positive
> - unique
> - stable
> - persisted
> - allocated once
> - reused across reprovision/restart
> - not client-controlled
>
> Do not create a different 2 GiB quota per app or per Hosted Runner.
>
> Free account disk means:
>
> ONE aggregate 2048 MiB project quota.
>
> MIGRATION
> =========
>
> Existing accounts/Hosted Runners may already have a valid HostedRunner.StorageProjectId.
>
> Design a safe migration.
>
> Prefer preserving the existing project ID where ownership is unambiguous rather
> than allocating a new one unnecessarily.
>
> Do not manually mutate production state.
>
> Do not require immediate deletion of HostedRunner.StorageProjectId if keeping a
> temporary compatibility field makes migration safer.
>
> But define clearly which field becomes authoritative.
>
> IMPLEMENTATION
> ==============
>
> Add the minimum account/subscription storage-allocation model required.
>
> Possible concept:
>
> AccountStorageAllocation
> {
>     AccountId
>     StorageProjectId
>     DiskLimitMb
> }
>
> or equivalent existing domain style.
>
> Ensure Hosted Runner provisioning resolves:
>
> account
> → authoritative StorageProjectId
> → DiskLimitMb from SubscriptionResourcePolicy
> → infrastructure command
>
> not an independently allocated Hosted Runner quota.
>
> TESTS
> =====
>
> Prove:
>
> 1. one account gets one stable StorageProjectId.
> 2. repeated Hosted Runner reprovision reuses it.
> 3. multiple Hosted Runners for same account, if model permits them, do not get
>    independent disk quotas.
> 4. two accounts do not share a project ID.
> 5. project IDs remain unique.
> 6. client cannot specify project ID.
> 7. legacy HostedRunner project ID migrates/preserves correctly.
> 8. DiskLimitMb comes from resource policy.
> 9. deletion/recreation of a Hosted Runner does not silently allocate another
>    independent quota for the same account.
>
> VALIDATION
> ==========
>
> Run relevant:
>
> - domain tests
> - API tests
> - Worker tests
> - infrastructure contract tests
> - architecture tests
> - Release builds
> - git diff --check
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. new storage ownership model
> 2. authoritative field/entity
> 3. migration behavior
> 4. legacy compatibility
> 5. project-ID allocation behavior
> 6. disk-limit derivation
> 7. tests
> 8. validation
> 9. branch
> 10. commit
> 11. PR number
>
> Do NOT move app volumes yet.
> Stage 4 — Put Hosted Runner storage under account quota
> Continue from merged account-level StorageProjectId work.
>
> Start from current canonical main.
>
> OBJECTIVE
> =========
>
> Make Hosted Runner persistent system/user storage consume the account-level disk
> quota.
>
> Target invariant
> ================
>
> For an account:
>
> Account StorageProjectId = X
> DiskLimitMb = 2048
>
> Hosted Runner system storage:
> projid = X
>
> Hosted Runner user storage:
> projid = X
>
> Both contribute to the SAME XFS project quota.
>
> There must not be an independent Hosted Runner disk quota separate from the
> account quota.
>
> IMPLEMENTATION
> ==============
>
> Update provision/reprovision contracts as needed so the Infrastructure Runner receives:
>
> - account StorageProjectId
> - account DiskLimitMb
>
> Ensure:
>
> - system volume root gets account projid
> - user volume root gets account projid
> - exact account hard quota is established
> - final postconditions are verified
> - reprovision preserves same account project
> - provisioning fails closed if quota assignment cannot be established
>
> Do not weaken the real-host verified XFS checks from Stage 1D.
>
> COMPATIBILITY
> =============
>
> Preserve existing Hosted Runner lifecycle behavior.
>
> Avoid unnecessary DB renames if compatibility migration is still in progress.
>
> But the source of truth must be account-level.
>
> TESTS
> =====
>
> Prove:
>
> 1. system storage uses account project.
> 2. user storage uses account project.
> 3. both share one project.
> 4. quota equals account policy disk limit.
> 5. reprovision uses same project.
> 6. project/quota failure fails provisioning.
> 7. no second quota is created for Hosted Runner.
> 8. Infrastructure Runner remains outside tenant project.
> 9. account A and B remain isolated.
>
> VALIDATION
> ==========
>
> Run:
>
> - infrastructure action tests
> - Worker provisioning tests
> - Runner tests
> - API tests
> - architecture tests
> - affected builds
> - Python compilation
> - git diff --check
>
> If appropriate, perform focused real-host acceptance after merge/deploy:
>
> - verify project ID
> - verify quota
> - verify same account identity
>
> Do not manually repair production state.
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. authoritative account StorageProjectId
> 2. provisioning payload
> 3. system/user projid behavior
> 4. quota behavior
> 5. reprovision behavior
> 6. tests
> 7. validation
> 8. real-host evidence if performed
> 9. branch
> 10. commit
> 11. PR number
>
> Do NOT move app storage in this stage.
> Stage 5 — Put app persistent volumes under the same account quota
> Continue from merged account-level Hosted Runner storage work.
>
> Start from current canonical main.
>
> OBJECTIVE
> =========
>
> Make ALL persistent app storage owned by an account consume the SAME account XFS
> project quota as the Hosted Runner.
>
> This is the critical aggregate disk milestone.
>
> Target
> ======
>
> Free account:
>
> Disk allowance = 2048 MiB
>
> ONE project ID:
>
> Account.StorageProjectId = X
>
> The following all contribute to X:
>
> - Hosted Runner system storage
> - Hosted Runner user storage
> - App A persistent data
> - App B persistent data
> - App C persistent data
> - every account-owned persistent app volume
>
> Do NOT create an independent XFS quota per app or volume.
>
> INVENTORY FIRST
> ===============
>
> Trace all current app persistent-volume creation paths.
>
> Identify:
>
> - Docker named volumes
> - bind mounts
> - application data paths
> - runtime data paths
> - uploaded-file paths
> - any dynamically created writable volume
>
> Determine which are tenant/account-owned.
>
> IMPLEMENTATION
> ==============
>
> When Hosted Runner provisions an app volume:
>
> 1. resolve the app's account.
> 2. resolve account StorageProjectId.
> 3. create/materialize the final volume path.
> 4. assign the account project ID recursively where required.
> 5. verify final projid.
> 6. only then start/attach the app container.
>
> Do not trust the app/client to supply project IDs or quota values.
>
> The app container should receive storage already prepared by the trusted
> infrastructure/host path.
>
> If the current Hosted Runner lacks privilege to safely assign host XFS project IDs,
> route storage preparation through the existing trusted Infrastructure Runner path.
>
> Do not grant Docker socket/host privilege to app containers.
>
> FAIL CLOSED
> ===========
>
> App deployment must not succeed with an unbounded writable persistent volume.
>
> If quota/project assignment cannot be established:
>
> deployment fails.
>
> TESTS
> =====
>
> Prove:
>
> 1. app persistent volume uses account project ID.
> 2. two apps from same account share same project.
> 3. apps from different accounts use different projects.
> 4. Hosted Runner + apps share one aggregate project.
> 5. app cannot request its own quota/project.
> 6. volume project-assignment failure fails deployment.
> 7. app deletion only removes its own storage.
> 8. deleting one app does not alter account quota identity.
> 9. reprovision/redeploy preserves correct account ownership.
> 10. no independent app-level 2 GiB quota exists.
>
> VALIDATION
> ==========
>
> Run:
>
> - Hosted Runner runtime tests
> - app deployment tests
> - infrastructure storage tests
> - API/Worker tests
> - architecture tests
> - builds
> - git diff --check
>
> After merge and canonical deployment, if safe, real-host verify:
>
> - create/use two app volumes
> - both have same account projid
> - XFS aggregate usage increases accordingly
>
> Do not manually patch project IDs.
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. app storage paths
> 2. project assignment path
> 3. trusted component responsible for assignment
> 4. same-account aggregation proof
> 5. cross-account isolation proof
> 6. failure behavior
> 7. tests
> 8. validation
> 9. real-host evidence if performed
> 10. branch
> 11. commit
> 12. PR number
> Stage 6 — Close /tmp and writable-root disk escape paths
> Continue from merged aggregate app-volume quota work.
>
> Start from current canonical main.
>
> OBJECTIVE
> =========
>
> Prevent app containers from bypassing account disk quota through:
>
> - Docker writable root filesystem
> - /tmp
> - arbitrary writable filesystem paths
>
> Target filesystem model
> =======================
>
> App container:
>
> /automaton/app
>     read-only runtime artifact
>
> /automaton/data
>     writable
>     account XFS quota-backed
>
> /tmp
>     tmpfs
>     explicitly size-bounded
>
> other writable persistent paths
>     explicit account-quota-backed mounts only
>
> Root filesystem:
>     read-only where compatible
>
> IMPORTANT
> =========
>
> /tmp should not be an unlimited persistent-disk escape hatch.
>
> Preferred model:
>
> /tmp = bounded tmpfs
>
> This consumes memory/cgroup resources rather than account persistent disk.
>
> If an app/build genuinely needs disk-backed temporary storage, it should use an
> explicit account-owned path such as:
>
> /automaton/tmp
>
> which is backed by account XFS quota.
>
> INVENTORY
> =========
>
> Audit current app runtime container:
>
> - rootfs writable/read-only
> - /tmp mount
> - /var/tmp
> - HOME
> - framework/runtime caches
> - package caches
> - logs
> - any location requiring writes
>
> Do not blindly enable read-only rootfs until required writable paths are understood.
>
> IMPLEMENTATION
> ==============
>
> Move toward:
>
> - read_only container rootfs
> - explicit writable account-backed data mounts
> - bounded tmpfs for /tmp
> - optionally bounded tmpfs for other genuinely ephemeral runtime paths
>
> Choose a conservative initial /tmp limit based on current runtime requirements.
>
> Do not expose host paths unnecessarily.
>
> Use:
>
> nodev/nosuid/noexec
>
> where compatible, but do not break legitimate runtime behavior without evidence.
>
> TESTS
> =====
>
> Prove:
>
> 1. rootfs is read-only.
> 2. app can write to /automaton/data.
> 3. /automaton/data counts toward account project quota.
> 4. /tmp is tmpfs.
> 5. /tmp has an explicit maximum size.
> 6. writing arbitrary rootfs path fails.
> 7. /tmp exhaustion does not consume persistent account disk.
> 8. /tmp pressure remains bounded by memory/cgroup behavior.
> 9. legitimate app runtime still starts and operates.
> 10. no Docker writable-layer disk bypass remains.
>
> VALIDATION
> ==========
>
> Run:
>
> - runtime container tests
> - app deployment tests
> - filesystem/security tests
> - Runner tests
> - architecture tests
> - builds
> - git diff --check
>
> After merge/deployment, perform safe real-host acceptance if appropriate.
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. rootfs policy
> 2. writable paths
> 3. /tmp implementation
> 4. /tmp size
> 5. other tmpfs paths
> 6. persistent disk paths
> 7. quota integration
> 8. tests
> 9. validation
> 10. branch
> 11. commit
> 12. PR number
> Stage 7 — Put build workspace/cache under account disk quota
> Continue from merged runtime filesystem hardening.
>
> Start from current canonical main.
>
> OBJECTIVE
> =========
>
> Prevent builds from bypassing the account disk allowance.
>
> All disk-backed build data owned by the account should consume the same account
> XFS project quota.
>
> Inventory:
>
> - source checkout/workspace
> - build output
> - temporary build files
> - dependency cache
> - package cache
> - Docker/build context
> - artifact staging
> - logs if disk-backed
>
> TARGET
> ======
>
> For account StorageProjectId X:
>
> Hosted Runner storage
> + app storage
> + build workspace
> + disk-backed build cache
> + disk-backed temporary build data
>
> all consume project X.
>
> Build container /tmp may be bounded tmpfs where appropriate.
>
> IMPLEMENTATION
> ==============
>
> Ensure ephemeral build containers use:
>
> workspace:
> account XFS project-backed storage
>
> cache:
> account XFS project-backed if retained
>
> /tmp:
> bounded tmpfs
>
> rootfs:
> read-only where feasible
>
> Do not create unbounded host directories for builds.
>
> Build cleanup should remove temporary data after completion, but quota enforcement
> must remain the hard safety boundary even if cleanup fails.
>
> TESTS
> =====
>
> Prove:
>
> 1. build workspace uses account project.
> 2. retained build cache uses account project.
> 3. build from account A cannot consume account B quota.
> 4. build disk growth is reflected in account XFS usage.
> 5. quota exhaustion causes a controlled build failure.
> 6. cleanup reduces usage.
> 7. failed cleanup cannot exceed the account hard quota.
> 8. no independent build quota multiplies account allowance.
> 9. /tmp behavior is bounded.
> 10. Infrastructure Runner storage remains outside tenant quota.
>
> VALIDATION
> ==========
>
> Run relevant build/runtime/infrastructure tests and builds.
>
> After merge/deployment, perform safe aggregate disk acceptance if possible.
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. build disk paths
> 2. account-project assignment
> 3. cache policy
> 4. cleanup policy
> 5. quota exhaustion behavior
> 6. tests
> 7. validation
> 8. branch
> 9. commit
> 10. PR number
> Stage 8 — Make CPU/memory/PIDs subscription-derived
> Continue from merged aggregate disk work.
>
> Start from current canonical main.
>
> OBJECTIVE
> =========
>
> Make compute limits authoritative from SubscriptionResourcePolicy.
>
> Current Free intent:
>
> CPU:
> 0.5 core
>
> Memory:
> 768 MiB
>
> Tasks:
> 128
>
> The aggregate tenant boundary should cover:
>
> - Hosted Runner control container
> - app containers
> - transient/build containers
>
> Infrastructure Runner remains outside.
>
> ARCHITECTURAL RULE
> ==================
>
> These are account/tenant aggregate limits, not per-app grants.
>
> Three apps must not each independently receive the full Free allowance.
>
> Use the existing aggregate cgroup/slice architecture proven in Stage 1D.
>
> IMPLEMENTATION
> ==============
>
> Wire:
>
> SubscriptionResourcePolicy
> → persisted effective account/Hosted Runner resource specification
> → provisioning/reprovisioning
> → systemd/cgroup limits
>
> Ensure no client can increase these values.
>
> Preserve deterministic reprovision behavior.
>
> TESTS
> =====
>
> Prove:
>
> 1. Free policy generates expected CPU.
> 2. Free policy generates expected memory.
> 3. Free policy generates expected TasksMax.
> 4. aggregate slice receives those values.
> 5. apps/build workloads join same aggregate slice.
> 6. Infrastructure Runner remains outside.
> 7. app cannot request higher CPU/memory than subscription.
> 8. multiple apps do not multiply account allowance.
> 9. reprovision restores policy values.
> 10. invalid/missing policy fails safely.
>
> VALIDATION
> ==========
>
> Run:
>
> - subscription tests
> - Worker provisioning tests
> - Runner/cgroup tests
> - API tests
> - architecture tests
> - Release builds
> - git diff --check
>
> Real-host acceptance after merge should confirm actual slice values.
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. authoritative compute policy
> 2. Free values
> 3. persisted specification
> 4. aggregate cgroup behavior
> 5. app/build membership
> 6. client override prevention
> 7. tests
> 8. validation
> 9. real-host evidence
> 10. branch
> 11. commit
> 12. PR number
> Stage 9 — Add account resource usage API and Studio observability
> Continue from merged resource-policy enforcement work.
>
> Start from current canonical main.
>
> OBJECTIVE
> =========
>
> Expose the account's effective subscription limits and authoritative resource usage.
>
> Do not change enforcement semantics in this stage.
>
> API should expose approximately:
>
> Plan:
> Free
>
> CPU limit:
> 0.5
>
> Memory limit:
> 768 MiB
>
> Disk limit:
> 2048 MiB
>
> Tasks limit:
> 128
>
> Disk usage:
> authoritative XFS project usage
>
> Optionally:
>
> - app count / max app count
> - Hosted Runner status
> - storage verification status
>
> IMPORTANT
> =========
>
> Account disk usage must come from the authoritative XFS project measurement, not
> from summing approximate DB values.
>
> Per-app usage may be informational if available, but the subscription enforcement
> number is the aggregate account project.
>
> STUDIO
> ======
>
> Add a simple resource section consistent with the existing Studio design system.
>
> Example information:
>
> Plan: Free
>
> Storage:
> 36 MiB / 2 GiB
>
> CPU limit:
> 0.5 core
>
> Memory:
> 768 MiB
>
> Applications:
> N / limit
>
> Do not create an elaborate billing UI.
>
> Handle:
>
> - measurement unavailable
> - measurement stale
> - over quota
> - runner unavailable
>
> clearly.
>
> TESTS
> =====
>
> Prove:
>
> 1. API returns policy limits.
> 2. disk usage uses authoritative measurement.
> 3. clients cannot spoof limits.
> 4. unavailable measurement is explicit.
> 5. Studio renders correct values.
> 6. Studio does not present stale estimate as authoritative.
> 7. existing admin/runner pages remain functional.
>
> VALIDATION
> ==========
>
> Run API/Studio/unit/architecture/build validation.
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. API contract
> 2. authoritative data sources
> 3. Studio changes
> 4. stale/unavailable behavior
> 5. tests
> 6. validation
> 7. branch
> 8. commit
> 9. PR number
> Stage 10 — Define upgrade/downgrade semantics
> Continue from merged resource policy + observability work.
>
> Start from current canonical main.
>
> This stage prepares the architecture for future paid plans.
>
> Do NOT add a paid plan unless explicitly required.
>
> OBJECTIVE
> =========
>
> Define and implement safe resource-policy transitions when a subscription changes.
>
> Examples:
>
> Upgrade:
> 2 GiB → 10 GiB
>
> Downgrade:
> 10 GiB → 2 GiB while current usage may be above 2 GiB
>
> UPGRADE
> =======
>
> Expected general flow:
>
> 1. subscription entitlement changes
> 2. effective resource policy changes
> 3. persist new effective policy
> 4. update XFS quota
> 5. verify
> 6. update aggregate cgroup limits
> 7. verify
>
> Do not briefly grant unbounded resources.
>
> DOWNGRADE
> =========
>
> Never delete user data merely because new disk limit is below current usage.
>
> If:
>
> current usage > new hard allowance
>
> define an explicit OverQuota state/policy.
>
> Preferred behavior:
>
> - preserve existing data
> - surface over-quota state
> - block or restrict new disk growth
> - optionally block deploy/build requiring additional space
> - allow deletion/cleanup
> - return to normal once under limit
>
> Do not depend on accidental kernel/XFS behavior as the product policy.
>
> CPU/memory downgrade behavior should also be defined.
>
> MODEL
> =====
>
> Add the smallest necessary account resource state, for example:
>
> Normal
> OverQuota
> ApplyingPolicy
> PolicyError
>
> only if needed.
>
> Avoid generic billing/workflow frameworks.
>
> TESTS
> =====
>
> Prove:
>
> 1. disk upgrade increases exact XFS hard quota.
> 2. compute upgrade changes aggregate cgroup.
> 3. safe disk downgrade under current usage works.
> 4. downgrade below current usage preserves data.
> 5. over-quota account cannot grow disk beyond intended policy.
> 6. cleanup can recover from over-quota.
> 7. app data is never automatically deleted.
> 8. policy application failure is visible/recoverable.
> 9. reprovision preserves new policy.
> 10. subscription/client cannot bypass transition rules.
>
> VALIDATION
> ==========
>
> Run full relevant subscription/resource/infrastructure tests.
>
> FINAL REPORT
> ============
>
> Report:
>
> 1. upgrade contract
> 2. downgrade contract
> 3. over-quota behavior
> 4. disk behavior
> 5. compute behavior
> 6. data-safety guarantees
> 7. tests
> 8. validation
> 9. branch
> 10. commit
> 11. PR number
>
> I would run them in exactly that order. Stages 1–5 are the most important for answering your immediate question: “Does Free really mean one aggregate 2 GB disk allowance across the user's Hosted Runner and apps?” After Stage 5, the answer should be mechanically yes; Stages 6–7 then close the less obvious escape paths like rootfs, /tmp, and builds.
