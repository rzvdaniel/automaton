---
title: Add a Runner Host
order: 30
---

# Add a Runner Host

## Goal

Register a server that can host managed runner containers.

## Server prerequisites

Prepare the target server with:

- a supported operating system;
- Docker installed and accessible to the Infrastructure Runner;
- a fixed-capacity XFS filesystem mounted separately from `/`, with XFS project quota support enabled;
- Docker's data root and the active image-store root on that same filesystem. For Docker's containerd image store, configure and verify containerd's `root` as well as Docker's `data-root`;
- required ports available;
- outbound connectivity to the Automaton API;
- sufficient CPU, memory, and disk;
- the required Docker network;
- the configured runtime directories;
- permissions for the Infrastructure Runner to execute the required host operations.

The exact provisioning transport and runtime connectivity mode determine which network and directory settings apply.

Before enabling scheduling, verify the active paths on the host:

```text
docker info --format '{{.DockerRootDir}}'
docker info --format '{{json .DriverStatus}}'
findmnt --target <docker-data-root> --output TARGET,SOURCE,FSTYPE,UUID,OPTIONS
findmnt --target <containerd-root> --output TARGET,SOURCE,FSTYPE,UUID,OPTIONS
```

If Docker uses its classic storage driver, its data root contains the image store. When Docker reports a containerd snapshotter, the image and snapshot store can use containerd's separate `root` (normally configured in `/etc/containerd/config.toml`). Both roots must resolve to the same non-root XFS mount with project quotas enabled. Configure that filesystem with a fixed capacity; telemetry verifies the XFS type, mount identity, and quota options. Automaton matches Docker's configured containerd socket to the active containerd process and checks the actual storage roots during Infrastructure Runner storage telemetry; ambiguous or unverified layouts are rejected. Until a current Infrastructure Runner reports a verified layout, the host cannot receive Hosted Runner placement or Docker image operations. After rollout, existing Infrastructure Runner host measurements are marked stale and must be refreshed by the updated Runner before the host is eligible again.

An administrator must prepare or reuse the fixed-capacity filesystem and configure both roots explicitly. Formatting, mounting, changing Docker/containerd configuration, and moving existing data are host setup operations; Automaton does not perform them as repair. Docker named volumes remain under their existing XFS project quotas. If they share this filesystem, its physical capacity bounds tenant volumes and platform image/cache storage together; it does not create a separate image-only quota. Automaton retains host free-space reserves and periodically prunes unused images older than 24 hours.

## Where

Admin → Runners → Runner Hosts

Host records can include a name, provider, region, public IP, private IP, provisioning transport, runtime connectivity mode, and capacity values. Use stable names such as `hetzner-fsn1-runner-01` or `runtime-local-docker`. Public Host Port connectivity requires a usable public IP; Local Docker Network is intended for local development.

Capacity includes total and reserved CPU, memory, and disk. Schedulable capacity is calculated as:

```text
Available capacity = Total - Reserved - Existing runner allocations
```

Disk admission counts each Hosted Runner's specification XFS disk limit, separately bounded writable root layer, and configured maximum Docker log storage. The root layer and Docker logs are platform overhead outside the account XFS project quota.

## Onboarding sequence

Administrators normally add remote Runner Hosts by installing and enrolling an Infrastructure Runner. The Infrastructure Runner reports host capabilities to Automaton and keeps the host record updated. Follow the detailed [Infrastructure Runner installation and maintenance](./infrastructure-runners.md) guide for the generated command and host-side procedure.

[![Runner Hosts via Infrastructure Runner](./assets/runner-hosts-via-infrastructure-runners.png)](./assets/runner-hosts-via-infrastructure-runners.png)

1. Open **Admin → Runners → Runner Hosts**.
2. Choose **Install Infrastructure Runner** for the target host and run the provided installer on the target server.
3. Wait for enrollment and confirm that the Infrastructure Runner appears online.
4. Validate the host. Validation is read-only: it reports missing prerequisites and does not silently repair the server. The installer and Infrastructure Runner setup perform the installation work; there is no separate **Prepare host** action in the current admin UI.
5. Approve the host when the administrator trusts it for managed workloads.
6. Approval automatically enables Hosted Runner scheduling.

## Ready conditions

A host is scheduling-eligible only when the Infrastructure Runner is online, validation succeeded, approval is complete when required, scheduling is enabled, the host is active, and enough capacity remains for the requested specification.
