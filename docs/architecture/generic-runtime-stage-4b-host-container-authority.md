# Generic Runtime Stage 4B: host-container authority

## Stage 4B1

Stage 4B1 defines an additive protocol boundary for future Standard Hosted
Runner Generic Runtime execution. It does not switch production deployments,
change provisioning, or alter accepted Privileged Host protocol-3 execution.

## Trust boundaries

- Standard Hosted Runners remain tenant-facing and do not receive `/host`,
  `/var/run/docker.sock`, host networking, host PID access, or privileged mode.
- The API authenticates the Runner and binds the Hosted Runner, Runner Host,
  application, and deployment target to trusted persisted state.
- An Infrastructure Runner is the trusted host boundary and may advertise the
  `HostContainerExecution` capability.
- The host Docker daemon remains behind the Infrastructure Runner.

Protocol 4 (`HostContainerExecution`) is distinct from protocol 3
(`ContainerApplicationExecution`). A protocol-4 Runner is not automatically an
Infrastructure Runner; purpose and capability checks remain mandatory.

## Typed boundary

The `HostContainerExecutionRequest` model accepts a fixed operation kind,
four-part resource scope, structured application command data, and trusted
resource kinds. It does not accept Docker argument arrays, arbitrary host
paths, arbitrary volume names, or arbitrary networks. The API supplies resource
kinds only after binding the request to persisted server state.

Stage 4B1 implements image resolution through the trusted host Docker boundary:
the Infrastructure Runner pulls the requested image and returns its repository
`RepoDigest`, never the image config ID. Other lifecycle operations are
validated as typed requests but fail closed until Stage 4B2 supplies the
trusted workspace/resource mapping.

The build operation is implemented in 4B2b. The future operations are application-container creation/start,
inspection, bounded logs, and removal. They must create and validate Automaton
ownership labels and derive networks and host paths from trusted server state.

## Ownership and lifecycle

The Hosted Runner remains the logical owner of deployment state, readiness,
candidate promotion, runtime registry, and reconciliation intent. The
Infrastructure Runner is only a typed transport for privileged Docker work.
Stage 4B2 must preserve idempotent resource identities, cleanup on failed
creation, restart/adoption, and the existing private runtime network.

The current API-instance/SignalR ownership model remains unchanged in 4B1;
distributed affinity changes are outside this stage.

## Stage 4B2a: authenticated ResolveImage relay

Stage 4B2a adds a narrow authenticated request path for image resolution only:

```text
Standard Hosted Runner
        | authenticated SignalR request
        v
API: bind caller, app/target, Hosted Runner and Runner Host
        | server-derived scope
        v
same-host Infrastructure Runner
        | HostContainerExecution
        v
host Docker daemon
```

The API selects the Infrastructure Runner from the persisted Runner Host
association. The Hosted Runner cannot select that Runner or supply an
authorization scope. The Infrastructure Runner must be online, protocol 4,
and advertise `HostContainerExecution`. Only `ResolveImage` is enabled; the
result is a Docker repository `RepoDigest`.

The relay does not provide a generic Runner-to-Runner RPC mechanism, does not
use `RunInfrastructureCommand`, and does not switch Standard Hosted runtime
execution. The current deployment path still uses its existing runtime
selection. The production SignalR/CAP topology continues to require one
active `automaton.api.v1` consumer until distributed affinity is implemented.

## Stage 4B2b: trusted Hosted build storage and RunBuild

Stage 4B2b extends the same authenticated relay for `RunBuild`. The API binds
the request to persisted Hosted Runner/app/target state and supplies resource
kinds (`source`, `artifact`, and `data`); the Hosted Runner cannot supply paths,
volume names, or filesystem identities.

The Infrastructure Runner derives the existing user volume
`automaton-hosted-runner-<HostedRunnerId:N>-user`, verifies its ownership labels,
and uses the deterministic layout:

```text
workspace/runner/deployments/<AppId:D>/<DeploymentTargetId:D>/<ExecutionAttemptId:D>/
  source/
```

The build container receives only the per-attempt incoming source subpath
read-write through a Docker volume mount.
The incoming source is isolated by the trusted execution-attempt directory; it
is not the accepted durable source. Build output remains in that attempt
workspace until the existing artifact selector and candidate-promotion flow
consumes it. It has `no-new-privileges`,
all capabilities dropped, no Docker socket, no `/host`, and no access to the
entire Hosted Runner volume. Build containers are labeled with the trusted
scope and are ephemeral; cleanup never removes source, data, or the user
volume. Application code never receives Docker authority.

The existing `/automaton/storage` user volume remains the persistence boundary,
including the Hosted Runner workspace. No host bind path or future Hetzner
Volume is introduced here. Application-container lifecycle, runtime adoption,
and switching ordinary Standard Hosted deployment remain deferred to 4B2c.

## Stage 4B2c: brokered Standard Hosted runtime

For a Managed Hosted Runner with the `Standard` application profile, the
Runner runtime manager uses `BrokeredApplicationContainerRuntime`. A
`PrivilegedHost` Runner and a Standard self-hosted Runner retain direct Docker
execution. Brokered build requests carry the application, deployment target,
and server-issued execution-attempt identifiers. Runtime lifecycle requests
carry the application and target identifiers, while candidate identity and
storage remain typed values instead of being inferred from workspace paths.

Create establishes the candidate and may not know an effective host port.
Start verifies ownership, starts the verified container, re-inspects it, and
returns Docker's effective host port. The Hosted Runner persists that port
before readiness and promotion. Reconciliation performs a scoped inspect,
prefers the inspected port over stale metadata, and adopts the same healthy
container ID after a Hosted Runner restart.

The persisted Runner Host private IPv4 address is supplied to newly
provisioned and reprovisioned Hosted Runner containers as
`AUTOMATON_RUNTIME_HOST_ADDRESS`. The Infrastructure Runner uses the same
address as Docker's explicit publication address. Loopback, wildcard, and
public addresses are rejected by the provisioning and execution boundaries.

Production rollout remains gated on the isolated Linux-host acceptance probe:
automatic publication on the real private address, post-start discovery,
Hosted Runner reachability, absence of unintended public exposure, and
distinct ports for simultaneous containers.
