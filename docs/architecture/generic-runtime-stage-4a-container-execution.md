# Generic Runtime Stage 4A: container execution

Stage 4A makes managed Generic Runtime execution container-based. The Runner
orchestrates application containers; it is not the application execution
environment and does not select a language or framework.

## Contract

`application.runtime.image` is required when `runtime.command` is declared for
new container deployments. Historical persisted contracts remain structurally
readable, but cannot be deployed until an image is explicitly declared.
`application.build.image` is optional and falls back to `runtime.image` when a
build command is present. Image references are resolved through Docker and the
resolved digest is retained in runtime metadata. Image identity is separate
from Automaton Git revision provenance.

The application owns its command and HTTP process. The container receives the
effective `AUTOMATON_PORT` and the stable `/automaton/data` value in
`AUTOMATON_APP_DATA_DIR`. Durable data is mounted separately from the
read-only application artifact.

## Runner boundary

Builds run in ephemeral containers with only the source workspace mounted.
Managed runtime containers use deterministic Automaton labels, a read-only
artifact mount, an explicit persistent-data mount, readiness checks, and the
existing candidate/promotion lifecycle. They do not receive the Docker socket,
Runner credentials/configuration, host root, host PID namespace, or privileged
mode. `no-new-privileges` and dropped Linux capabilities are applied.

The existing runtime registry and deployment metadata remain the source of
current/candidate readiness and routing state. Stage 4A adds container identity,
requested image, and resolved image digest to that metadata. On restart, only
Runner-labelled containers with matching app/target/runtime-kind identity are
eligible for reconciliation; arbitrary containers are not adopted. Candidate
metadata is reconciled as well as current metadata, and a resolved digest is
retained for future recovery.

When the Runner itself is containerized, `AUTOMATON_HOST_WORKSPACE_ROOT` must
name the host path corresponding to its `AUTOMATON_WORKSPACE_ROOT` mount. Only
paths inside that configured Runner workspace are translated for child Docker
bind mounts. There is no `/host` path inference. Application containers use an
isolated bridge network and loopback-only host publication; they never share the
Runner or host network namespace.

Actions and connected-flow artifact synchronization remain separate product
boundaries in this stage. Existing process metadata without an image is not
treated as a container and is not silently adopted; a deliberate redeployment
converts it to the container model.

The historical process manager remains in the source tree only for existing
focused compatibility tests during this staged migration. Production DI uses
`RunnerRuntimeContainerManager` for managed Generic Runtime execution.

## Deferred work

- Remove the now-unused process-manager implementation and migrate its focused
  tests once container-runtime coverage is complete.
- Add digest persistence to any API-visible deployment DTOs if product surfaces
  need to display resolved application image identity.
- Harden network policy and non-root image enforcement beyond the low-cost
  container boundary in this stage.
- Migrate Actions and connected-flow transport only in their planned stages;
  they are not application runtime containers.
