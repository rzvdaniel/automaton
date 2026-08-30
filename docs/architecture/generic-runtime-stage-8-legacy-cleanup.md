# Stage 8 — Legacy cleanup and generic-runtime guardrails

Stage 8 locks the boundary established by Stages 1–7. The generic application
contract, build executor, runtime supervisor, workspace boundary, deployment
state, and gateway remain stack-neutral. The deferred controller/FastAPI bridge
continues to exist as an explicitly legacy compatibility subsystem.

## Persisted deployment state

`AppDeploymentTarget.LegacyControllerRuntimeDeployed` is the compatibility
marker for the deferred controller runtime. It replaces the ambiguous
`RuntimeDeployed` name through the `Stage8NameLegacyControllerRuntimeState`
one-way migration. Existing rows keep their value; historical migration files
are not rewritten.

`AppDeploymentTarget.GenericRuntimeDeployed` is the independent marker for a
successfully promoted generic runtime. Generic routing, preview eligibility,
and generic deployment status consume this target-local marker. They do not
inspect the mutable application contract, legacy frontend/backend storage
metadata, or controller deployment state.

`LastSuccessfulDeploymentAt` remains general deployment history. It is not a
generic-runtime promotion timestamp and must not be relabeled as one.

## Generic boundary

Generic build/runtime services know only about structured commands, artifacts,
ports, health, storage, process lifecycle, and routing. They receive filesystem
locations through `RunnerWorkspaceService` and do not construct legacy
controller paths themselves. `RunnerWorkspaceService` retains its separate
legacy-controller path helpers only because the compatibility bridge still
needs them.

The canonical contract assembly has no dependency on API, infrastructure, or
runner assemblies and exposes no language, framework, frontend, backend,
Python, Vue, FastAPI, or runtime-type discriminator. Reflection guardrails in
the Runner test suite protect these dependency and public-contract boundaries.

## Deferred compatibility subsystem

The following remain intentionally deferred and must not be used as evidence
that a generic runtime exists:

- `AppActionManifest` and App Action deployment/execution;
- controller execution through `controller_runner.py` and the FastAPI tunnel;
- legacy controller backend/venv workspace paths;
- `RunnerBackendEnvironmentService` and Python diagnostics;
- controller-specific Studio/API models and frontend/backend file metadata.

Later cleanup may remove these pieces once their consumers and migration policy
are addressed. Stage 8 does not replace the controller implementation or add
new generic execution behavior.
