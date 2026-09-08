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
resource identities. It does not accept Docker argument arrays, arbitrary host
paths, arbitrary volume names, or arbitrary networks. The Stage 4B1 executor
fails closed until the Stage 4B2 server-side workspace/resource mapping exists.

Stage 4B1 implements image resolution through the trusted host Docker boundary:
the Infrastructure Runner pulls the requested image and returns its repository
`RepoDigest`, never the image config ID. Other lifecycle operations are
validated as typed requests but fail closed until Stage 4B2 supplies the
trusted workspace/resource mapping.

The future operations are build, application-container creation/start,
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

## Deferred to Stage 4B2b/4B2c

- server-derived per-Hosted-Runner workspace, artifact, and data mappings;
- build execution and application-container lifecycle;
- secure sibling build/runtime container mounts;
- private-network and host-port mapping for brokered operations;
- trusted storage/workspace mapping and `RunBuild` (4B2b);
- application-container lifecycle and `BrokeredApplicationContainerRuntime` (4B2c);
- Standard Hosted Runner Generic Runtime switch and production rollout (4B2c);
- migration and acceptance of Standard Hosted Runner protocol 4;
- retirement of direct Docker execution where it is no longer needed.
