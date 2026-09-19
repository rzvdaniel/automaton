# Automaton Runtime & Resource Isolation Roadmap

## Purpose

This roadmap defines the next implementation stages for Automaton's managed application runtime.

The target is a runtime model where:

- builds are reproducible and disposable;
- runtime containers are clean and recreatable;
- persistent user state lives only in explicit Automaton-managed storage;
- Hosted Runner plans represent real enforceable aggregate resource allocations;
- build, deploy, and runtime state is observable by both humans and AI agents through structured APIs/CLI output;
- the Infrastructure Runner remains outside tenant resource quotas;
- untrusted application containers never receive host-level control surfaces.

This document is a working roadmap. Stage boundaries and details may be adjusted as implementation teaches us more.

---

# Core architectural principles

## 1. Hosted Runner is an aggregate ownership boundary

A Hosted Runner allocation should cover:

- the Hosted Runner control container;
- its application runtime containers;
- its transient build containers;
- its tenant-owned build workspaces;
- its persistent application data;
- relevant deployment artifacts.

The Infrastructure Runner remains outside that user allocation.

Containers remain siblings on the host. We do not use nested Docker or Docker-in-Docker.

## 2. Build state is disposable

The build environment is temporary.

A deployment should conceptually follow:

```text
application definition
        ↓
temporary build container
        ↓
build artifact
        ↓
build container removed
        ↓
clean runtime container
```

A runtime deployment must not depend on state left behind in a build container.

## 3. Runtime state is reproducible

A runtime container should be recreatable from:

- the selected runtime OCI image;
- the immutable deployment artifact;
- explicit runtime configuration;
- persistent application data.

Replacing the runtime container should not destroy durable user state.

## 4. Persistent user state is explicit

The intended runtime storage contract is:

```text
/automaton/app
    deployed artifact
    read-only

/automaton/data
    persistent application data
    writable
    quota-controlled

/tmp
    optional bounded ephemeral scratch space

container root
    eventually read-only where compatible
```

## 5. AI agents should observe rather than shell into containers by default

The primary AI workflow should use structured build/deploy/runtime events and logs.

`automaton exec` may exist later as an advanced operational tool, but should not be required for normal AI build/deploy/debug workflows.

---

# Stage 1 — Hosted Runner aggregate resource boundary

## Goal

Turn Hosted Runner plan values into real host-enforced aggregate resource limits.

---

## Stage 1A — Hard aggregate storage quota

### Goal

Make `HostedRunnerResources.DiskLimitMb` a real hard storage limit using XFS project quotas.

### Current validated host capability

A development Runner Host has already proven:

- dedicated Hetzner Volume;
- XFS filesystem;
- `prjquota` enabled;
- project quota accounting and enforcement active;
- hard quota enforcement verified experimentally;
- Docker data root moved to the XFS workload volume;
- Docker writable-layer `--storage-opt size=...` is ineffective in the current Docker/containerd/overlayfs configuration.

Therefore Automaton must not rely on Docker writable-layer size limits for tenant storage enforcement.

### Desired storage ownership model

Conceptually:

```text
/srv/automaton-workloads/
  docker/
  hosted-runners/
    <hosted-runner-id>/
      apps/
        <app-id>/
          data/
      builds/
      artifacts/
      other-owned-storage/
```

The exact layout should follow current repository abstractions rather than being hardcoded from this example.

### Required behavior

- one stable storage ownership scope per Hosted Runner;
- one stable XFS project quota identity per Hosted Runner;
- `DiskLimitMb` maps to an XFS hard project quota;
- persistent app data belongs to the Hosted Runner quota scope;
- build workspace should belong to the same quota where practical;
- deployment artifacts should belong to the same quota if tenant-owned;
- quota creation/update is idempotent;
- plan upgrades can increase storage without recreating the Hosted Runner;
- quota decreases must never silently delete data;
- storage usage should be reported from authoritative filesystem/quota state where available;
- hosts without required quota support must fail capability validation rather than silently falling back to approximate enforcement.

### Explicit limitation

Docker image layers, shared caches, and arbitrary container writable-root consumption are not automatically attributable to a Hosted Runner through this quota.

Those remain host-safety concerns unless later stages move writable runtime activity into explicit controlled paths.

---

## Stage 1B — Aggregate CPU, memory, and PID boundary

### Goal

Enforce CPU, memory, and PID limits across the whole Hosted Runner ownership boundary rather than per sibling container.

### Desired topology

```text
Hosted Runner A resource group
  ├── Hosted Runner control container
  ├── App A
  ├── App B
  └── Build container

Hosted Runner B resource group
  ├── Hosted Runner control container
  └── App C

Infrastructure Runner
  outside user resource groups
```

### Preferred mechanism

Use hierarchical Linux cgroups / Docker `cgroup-parent` or the appropriate systemd-compatible equivalent after auditing the actual host configuration.

### Required aggregate limits

- CPU
- memory
- PIDs

### Semantics to preserve

- `MemoryReservationMb` may remain advisory/container-local if Linux semantics do not map cleanly to aggregate enforcement;
- `SharedMemoryMb` is likely container-local;
- `MaximumApps` remains control-plane policy;
- Docker log settings remain per-container/logging controls.

### Important invariant

Do not multiply a Hosted Runner's allocation across child containers.

For example, a Hosted Runner with `0.5 CPU` means the aggregate Hosted Runner-owned workloads share 0.5 CPU; it must not mean every sibling container independently gets 0.5 CPU.

---

## Stage 1C — Resource-boundary validation

### Required proof

#### Storage

Prove:

- one Hosted Runner receives one hard quota;
- multiple app directories count toward that same quota;
- aggregate writes cannot exceed the quota;
- quota increase works;
- two Hosted Runners have independent quota scopes;
- usage reporting is accurate.

#### CPU / memory / PIDs

Prove:

- Hosted Runner control container is placed in the correct resource group;
- app containers join the same group;
- build containers join the same group;
- another Hosted Runner gets a different group;
- Infrastructure Runner remains outside;
- aggregate CPU/memory/PID limits are configured and observable.

Prefer deterministic cgroup inspection plus controlled behavioral tests rather than flaky performance assertions.

---

# Stage 2 — Reproducible ephemeral build pipeline

## Goal

Make the build phase explicitly disposable and reproducible.

### Desired lifecycle

```text
source/application definition
        ↓
create temporary build workspace
        ↓
create temporary build container
        ↓
run build.command
        ↓
capture build.outputDirectory
        ↓
validate artifact
        ↓
remove build container
        ↓
remove temporary workspace
        ↓
retain artifact + logs/metadata
```

### Resource ownership

The build container must belong to the same Hosted Runner aggregate resource boundary.

Therefore:

- build CPU counts against Hosted Runner CPU;
- build memory counts against Hosted Runner memory;
- build PIDs count against Hosted Runner PID limit;
- build workspace counts against Hosted Runner storage quota.

### Required properties

- build container state is never required after successful build;
- package installation inside the build container is allowed because the container is disposable;
- only produced artifact/output becomes deployable runtime content;
- build failure preserves logs and operation metadata, not the container itself;
- failed build workspaces may optionally be retained briefly for diagnostics, but must remain quota-controlled and time-bounded;
- build cleanup must be safe and idempotent.

### Package semantics

Application/package dependencies are installed during build where appropriate:

- NuGet;
- npm;
- Python dependencies;
- frontend tooling;
- other build-time dependencies.

OS-level dependencies required at runtime must exist in the chosen runtime image or later be supported through an explicit image-build capability.

Do not rely on mutating the runtime container after deployment.

---

# Stage 3 — Clean runtime container lifecycle

## Goal

Ensure every deployment runs in a fresh, reproducible runtime container.

### Desired runtime model

```text
runtime.image
      +
immutable deployment artifact
      +
explicit runtime config
      +
persistent /automaton/data
      ↓
fresh runtime container
```

### Required properties

- deployment artifact mounted read-only at `/automaton/app`;
- persistent app data mounted at `/automaton/data`;
- runtime container may be deleted/recreated without losing durable data;
- runtime container never depends on build-container state;
- runtime package installation via ad hoc shell commands is not part of normal deployment semantics;
- runtime image remains the source of OS-level runtime dependencies;
- app runtime container remains a sibling container managed through trusted host-control operations.

### Cleanup

Old candidate/runtime containers should be removable after deployment cutover according to existing rollout semantics.

No persistent user state should live only in the container writable layer.

---

# Stage 4 — Runtime filesystem hardening

## Goal

Close the remaining writable-root disk-abuse and mutability gap.

### Target runtime filesystem contract

```text
/automaton/app
    read-only

/automaton/data
    writable
    persistent
    quota-controlled

/tmp
    writable only if needed
    ephemeral
    bounded

container root
    read-only where compatible
```

### Why this stage exists

Current Docker writable-layer size limits were experimentally shown to be ineffective in the current environment.

Therefore an untrusted app with a normal writable root filesystem could otherwise consume host disk outside `DiskLimitMb`.

### Implementation direction

Evaluate:

- Docker `--read-only`;
- bounded tmpfs for `/tmp`;
- explicit writable-path contract;
- runtime-specific temporary directory environment variables;
- representative .NET, Node, Python, and static/runtime compatibility.

### Desired end state

Durable writes go only through `/automaton/data`.

Temporary writes are bounded and ephemeral.

Everything else is immutable.

---

# Stage 5 — Structured build/deploy/runtime observability

## Goal

Make the full application lifecycle observable to AI agents and humans without requiring shell access inside containers.

This is more important to the normal AI workflow than `automaton exec`.

### Core principle

An AI should be able to:

```text
edit source
→ deploy
→ watch build
→ understand failure
→ edit source
→ redeploy
→ watch runtime
→ inspect application logs
→ verify health
```

without opening an interactive shell.

### CLI direction

Example future interface:

```bash
automaton deploy <app> --json

automaton deployments get <operation-id> --json
automaton deployments wait <operation-id> --json
automaton deployments logs <operation-id> --follow

automaton logs <app> --follow
```

Exact command names may change.

### Structured protocol

Prefer durable NDJSON / JSONL or equivalent machine-readable event streams.

Events should contain:

- operation ID;
- stable event type;
- sequence number;
- timestamp;
- app/deployment identity;
- stage;
- status;
- structured error information;
- stdout/stderr payload where applicable.

Do not require AI agents to parse human prose.

### Example build events

```text
BuildQueued
BuildStarted
BuildContainerCreated
BuildCommandStarted
BuildStdout
BuildStderr
BuildArtifactCreated
BuildCompleted
BuildFailed
BuildCleanupCompleted
```

### Example deployment events

```text
DeploymentQueued
RuntimeImageResolving
RuntimeImagePulled
RuntimeContainerCreating
RuntimeContainerStarted
HealthCheckStarted
HealthCheckPassed
DeploymentCutoverStarted
DeploymentCompleted
DeploymentFailed
```

### Runtime logs

The AI should be able to follow normal app stdout/stderr through a durable CLI/API surface.

Example:

```bash
automaton logs my-app --follow
```

### Important constraints

- local build activity may be ephemeral;
- remote deployment operations should be durable;
- no fake progress percentages;
- terminal failure state must be durable;
- retry should create a new operation rather than mutate failed history;
- CLI talks to the Automaton API, not directly to RabbitMQ or internal transport.

---

# Stage 6 — Disk-abuse and transient-storage controls

## Goal

Protect Runner Hosts from storage consumption that cannot be cleanly charged through Hosted Runner persistent quotas.

### Areas

- image size limits;
- build image pull limits/policies;
- shared image/cache GC;
- host free-space reserve;
- workload-volume reserve;
- Docker log rotation;
- build temporary-storage ceilings;
- stale artifact cleanup;
- failed-build retention policy;
- host-level low-space scheduling rules.

### Capacity model

Track OS/root disk and workload volume independently.

Conceptually:

```text
Root filesystem
  OS
  control-plane safety reserve

Workload XFS volume
  Docker data
  tenant-owned quota trees
  shared Docker infrastructure
  workload-volume reserve
```

The scheduler must never treat all free storage as tenant allocatable.

---

# Stage 7 — Resource and operational observability in Studio/API/CLI

## Goal

Expose the resource model clearly to operators and users.

### Hosted Runner views

Show:

- plan allocation;
- configured hard storage quota;
- current storage usage;
- CPU allocation/usage;
- memory allocation/usage;
- PID allocation/usage;
- max apps;
- host placement;
- health/schedulability;
- capacity rejection reason.

### Application views

Show where meaningful:

- persistent storage usage;
- runtime resource usage;
- deployment/build operations;
- runtime logs;
- current deployment;
- health state.

### Scheduler explainability

When provisioning is rejected, expose concrete reasons such as:

```text
Insufficient workload-volume reserve
Insufficient memory capacity
Insufficient CPU capacity
Host not quota-capable
Infrastructure Runner unavailable
```

Avoid generic "no capacity" errors when a specific reason is known.

---

# Stage 8 — Optional controlled app exec

## Goal

Provide an advanced operational command surface only if it proves valuable.

This is not required for normal AI build/deploy/debug workflows.

### Example

```bash
automaton exec my-app -- python manage.py migrate
automaton exec my-app -- dotnet MyTool.dll repair
automaton exec my-app -- ls -la /automaton/data
```

### Rules

- authenticated;
- ownership checked;
- command executes only inside the selected app container;
- no host shell;
- no Docker socket;
- no arbitrary host path access;
- auditable;
- not a persistent package-installation mechanism.

### Important semantic rule

`exec` operates an existing deployment.

It does not redefine the deployment.

If a command mutates the runtime container, that mutation is disposable and must not be treated as durable application configuration.

---

# Deferred — OCI image build capability

The current generic runtime model uses a supplied runtime OCI image plus an application artifact.

A future capability may allow Automaton to build application OCI images directly from a Dockerfile or generated image definition.

Possible later flow:

```text
source
+
Dockerfile / image definition
        ↓
Automaton image build
        ↓
immutable OCI image
        ↓
runtime
```

This could allow AI agents to define OS-level runtime dependencies without requiring externally prebuilt runtime images.

This is deliberately separate from the current artifact build pipeline.

---

# Deferred — Backup implementation

Backup work is not part of the stages above.

Current intended recovery model:

```text
user source/repository
+
Automaton control-plane metadata
+
persistent application data
        ↓
redeploy
        ↓
new runtime containers
```

Do not back up as durable application state:

- app containers;
- build containers;
- Docker image cache;
- container writable layers;
- transient build workspaces.

If Automaton later offers persistent-data backup, consistency semantics must be designed separately.

---

# Security invariants across all stages

Untrusted application/build contracts must never directly control:

- host filesystem paths;
- Docker socket;
- raw Docker arguments;
- cgroup paths;
- XFS project IDs;
- quota commands;
- host network names;
- arbitrary host shell commands.

Trusted Infrastructure Runner / host-control operations own host-level enforcement.

Application containers should continue to use appropriate hardening such as:

- `no-new-privileges`;
- dropped capabilities;
- no Docker socket;
- trusted mount construction;
- trusted image resolution;
- explicit runtime ports/networking.

---

# End-state architecture

Conceptually:

```text
Runner Host
│
├── Infrastructure Runner
│     host-control plane
│     outside tenant quotas
│
├── Hosted Runner A resource boundary
│     CPU hard aggregate
│     memory hard aggregate
│     PID hard aggregate
│     storage hard aggregate
│
│     ├── Hosted Runner control container
│     ├── ephemeral build container
│     ├── runtime App A
│     ├── runtime App B
│     └── quota-controlled tenant storage
│
└── Hosted Runner B resource boundary
      independent allocation
```

Application lifecycle:

```text
AI edits source
      ↓
automaton deploy
      ↓
ephemeral build container
      ↓
structured build events/logs
      ↓
immutable artifact
      ↓
build container removed
      ↓
fresh runtime container
      ↓
structured deployment events
      ↓
health passes
      ↓
runtime logs/metrics available
```

Runtime filesystem target:

```text
/automaton/app   read-only artifact
/automaton/data  persistent + hard quota
/tmp             bounded ephemeral scratch
/                read-only where compatible
```

---

# Recommended implementation order

1. Stage 1A — hard storage quota
2. Stage 1B — aggregate CPU/memory/PIDs
3. Stage 1C — validation
4. Stage 2 — explicit ephemeral build pipeline
5. Stage 3 — clean runtime lifecycle
6. Stage 4 — runtime filesystem hardening
7. Stage 5 — structured build/deploy/runtime observability
8. Stage 6 — disk-abuse/transient-storage controls
9. Stage 7 — Studio/API/CLI resource observability
10. Stage 8 — optional controlled app exec

The ordering can change if repository inspection shows that some stages are already substantially implemented.

---

# Definition of success

The platform should eventually guarantee:

> A Hosted Runner is a real tenant resource allocation boundary.

> Build environments are temporary and reproducible.

> Runtime containers are clean and replaceable.

> Persistent application state exists only in explicit Automaton-managed storage.

> AI agents can understand build, deployment, and runtime failures through structured machine-readable observability rather than shelling into hosts or containers.

> Host-control privileges remain isolated in trusted infrastructure components.

This roadmap is intentionally implementation-oriented and may evolve as each stage exposes additional constraints.
