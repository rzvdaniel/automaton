# Hosted Runner aggregate resource boundary: Stage 1

**Status:** Implemented in the current checkout; real-host validation remains required.

## Existing runtime boundary

Managed Standard Hosted Runners do not run application processes inside the
Hosted Runner control container. The Hosted Runner, application containers,
and build containers are sibling Docker containers on the Runner Host.
Application and build Docker operations are brokered through the existing
Infrastructure Runner `HostContainerExecution` boundary. The application
contract cannot provide host paths, Docker arguments, cgroup parents, or quota
identities.

## Stage 1A: storage

Each `HostedRunner` now has a persisted `StorageProjectId`. The control plane
starts with a SHA-256-derived candidate and linearly probes occupied IDs; the
database unique index prevents two Hosted Runners from sharing an identity.
Provision and reprovision actions receive only this server-derived ID and the
current `DiskLimitMb`.

The trusted Infrastructure Runner validates the deterministic, Automaton-owned
system and user volume labels and their Docker volume mountpoints. It requires
both volumes to be under the host Docker volume root on XFS with `prjquota` or
`pquota` enabled. It assigns both volume trees to the same XFS project and sets
the project hard block limit from `DiskLimitMb`. Repeating provisioning repairs
the association and updates the limit without replacing data. A decrease below
current project usage fails before the control container is replaced.

The quota includes the control Runner's system volume and the user volume,
which contains application persistent data, application workspace/artifacts,
application log files, home state, and brokered build source/workspace
storage. XFS project quota reporting is the authoritative usage source.

The quota does not include Docker image layers, shared Docker/build cache,
Docker daemon log files, Docker's container writable layers, or
infrastructure-runner storage. Docker
`--storage-opt size` is intentionally not used because it was empirically
ineffective on the validated Docker/containerd/overlayfs host.

## Stage 1B: CPU, memory, and PIDs

Provisioning requires cgroup v2 and Docker's `systemd` cgroup driver. It
creates/updates one persistent systemd slice named from the trusted Hosted
Runner ID and sets `CPUQuota`, `MemoryMax`, `MemorySwapMax=0`, and `TasksMax`.
The control container is started with that slice as its Docker cgroup parent.
Brokered build and application container creation derives the same parent from
the trusted Hosted Runner scope. A different Hosted Runner derives a different
slice. The Infrastructure Runner is never assigned a Hosted Runner slice.

`CpuLimit`, `MemoryLimitMb`, and `PidsLimit` are aggregate parent limits.
`MemoryReservationMb` remains a control-container advisory reservation;
`SharedMemoryMb` remains `/dev/shm` per-container. Docker log rotation remains
per-container, and `MaximumApps` remains a control-plane capacity policy.

## Deferred work and validation

This stage does not add backups, per-app hard quotas, image/cache garbage
collection, or Docker writable-layer limits. It does not migrate or mutate a
production Runner Host automatically. The supported-host capability checks and
focused action/runtime tests must be complemented by a controlled validation on
an XFS/prjquota host for quota usage, quota failure, cgroup placement, and
independent Hosted Runner boundaries.
