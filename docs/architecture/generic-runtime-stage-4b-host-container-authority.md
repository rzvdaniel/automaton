# Generic Runtime Stage 4B1: host-container authority

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

The future operations are image resolution, build, application-container
creation/start, inspection, bounded logs, and removal. They must create and
validate Automaton ownership labels and derive networks and host paths from
trusted server state.

## Ownership and lifecycle

The Hosted Runner remains the logical owner of deployment state, readiness,
candidate promotion, runtime registry, and reconciliation intent. The
Infrastructure Runner is only a typed transport for privileged Docker work.
Stage 4B2 must preserve idempotent resource identities, cleanup on failed
creation, restart/adoption, and the existing private runtime network.

The current API-instance/SignalR ownership model remains unchanged in 4B1;
distributed affinity changes are outside this stage.

## Deferred to Stage 4B2

- server-derived per-Hosted-Runner workspace, artifact, and data mappings;
- secure sibling build/runtime container mounts;
- private-network and host-port mapping for brokered operations;
- API/Hosted Runner dispatch wiring;
- migration and acceptance of Standard Hosted Runner protocol 4;
- retirement of direct Docker execution where it is no longer needed.
