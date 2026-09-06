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

The runtime process receives the effective `AUTOMATON_PORT` and the resolved
`AUTOMATON_APP_DATA_DIR`. It must own and serve its HTTP endpoint on that
boundary. Readiness is modeled by the generic health contract rather than by a
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

The generic deployment path is the Stage 1 path. Historical controller/action
execution remains behind its existing adapter paths only so the rest of the
repository can migrate incrementally. Those paths may retain Python,
`runtime/backend`, virtual-environment, controller, and FastAPI control-plane
details; none are part of `ApplicationContract` or generic runtime process
execution. New generic code must not depend on them.

## Stage 2 removal candidates

The following remain deliberately outside this Stage 1 change and are tracked
by the Stage 0 inventory:

- controller/action execution, including the Python module and controller
  tunnel bridge;
- legacy Runner workspace, dependency, and process helpers;
- split frontend/backend deployment state and capability checks;
- legacy CLI packaging/validation and API-side frontend/backend storage
  assumptions;
- framework-specific publishing and Studio deployment surfaces.

Stage 2 should remove or replace these callers after their ownership and
durable-state migration paths are mapped. It must not add new compatibility
fields or infer generic runtime behavior from legacy roles.

Related execution and storage details are documented in
`generic-runtime-stage-3-build-executor.md` and
`generic-runtime-stage-4-storage-boundaries.md`.
