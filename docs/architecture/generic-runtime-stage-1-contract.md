# Generic Runtime Stage 1: application contract

Stage 1 establishes the smallest stack-neutral contract for deploying an
application-owned runtime. The application package describes what to build,
how to start its HTTP process, how readiness is checked, and where durable
application data is stored. Automaton supplies orchestration, process
supervision, storage boundaries, routing, authorization, and deployment
lifecycle; it does not own the application's HTTP process.

## Contract

`src/Automaton.Core/Models/ApplicationContract.cs` is the source of truth.
Its shape is:

- `build`: optional structured command, output directory, and timeout;
- `runtime`: optional structured command and port; a web application must
  provide a runtime command;
- `health`: optional readiness path, port, and startup timeout;
- `storage`: durable application data directory and validated named mounts.

Commands are opaque executable, argument, working-directory, environment, and
shell settings. The contract does not select a language, framework, package
manager, internal source layout, build system, or application architecture.

The runtime process receives its container listening port in `AUTOMATON_PORT`
and the resolved `AUTOMATON_APP_DATA_DIR`. It must own and serve its HTTP
endpoint on that boundary and bind to `0.0.0.0` (or the equivalent
all-interfaces address) inside the container; binding only to `127.0.0.1`
inside the container is not reachable through Docker bridge publication.
Readiness is modeled by the generic health contract rather than by a
framework-specific probe. An Actions-only or other finite-work application
without an HTTP endpoint may omit the runtime command.

## Runner boundary

`RunnerDeploymentService` validates the contract, executes the optional build,
stages the selected artifact, and hands the declared runtime to
`RunnerRuntimeProcessManager`. The process manager starts the declared command
without interpreting its language or framework, supplies the runtime
environment, supervises the process, waits for readiness, and reports the
runtime state. Non-Python fixtures and architecture guardrail tests in
`src/UnitTests/Automaton.Runner.Tests/` protect this boundary.

## Temporary compatibility boundary

The generic deployment path is the Stage 1 path. Product-level Automaton
Action semantics remain valid product behavior: declared actions, execution
state and history, inputs and results, logs and artifacts, limits,
idempotency, and redaction are not legacy concepts merely because one current
executor is stack-specific. Any Action execution retained after the runtime
migration must use generic execution boundaries.

The temporary adapter is only the legacy controller architecture, retained so
the rest of the repository can migrate incrementally. It includes
`AppsControllerExecutionService` controller deployment/execution,
controller auto-deploy and tunnel behavior, `fastapi_server/controller_runner.py`,
the Python `handle(action, inputs)` convention, `runtime/backend`, target/app
virtual environments, requirements-driven controller setup, and
controller-specific FastAPI transport. None of these are part of
`ApplicationContract` or generic runtime process execution. New generic code
must not depend on them, and Stage 2 must not leave a second legacy production
execution architecture as a fallback.

## Stage 2 removal candidates

The following remain deliberately outside this Stage 1 change and are tracked
by the Stage 0 inventory:

- the legacy controller deployment/execution and transport paths listed above;
- legacy Runner workspace, dependency, and process helpers;
- split frontend/backend deployment state and capability checks;
- legacy CLI packaging/validation and API-side frontend/backend storage
  assumptions;
- durable-state migration required by removing those paths.

Stage 2 is the point at which generic runtime becomes the sole production
runtime architecture: it removes the legacy deployment state and execution
paths, migrates durable state, and eliminates controller, FastAPI, and
frontend/backend runtime dependencies. No legacy production execution fallback
remains. It must not delete the Automaton Actions product feature merely
because its current implementation contains Python-specific execution.

Stage 3 is the later product-surface cleanup: remove remaining legacy
API/CLI/Studio/public terminology and UX, obsolete user-facing contracts and
schema leftovers, and publishing or framework-specific product surfaces. Full
Studio and publishing cleanup is not a Stage 2 responsibility unless it is
needed to eliminate a production runtime dependency.

Related execution and storage details are documented in
`generic-runtime-stage-3-build-executor.md` and
`generic-runtime-stage-4-storage-boundaries.md`.
