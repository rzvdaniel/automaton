# Generic runtime Stage 8: legacy coupling inventory

Stage 8 begins from the cross-stack acceptance result in Stage 7. The generic
application path is already stack-neutral; this inventory records the
remaining terminology and implementation so cleanup does not accidentally
remove product state or the deferred controller/action subsystem.

## Classification

| Area | Current coupling | Classification | Stage 8 disposition |
| --- | --- | --- | --- |
| `Automaton.Core.Models.ApplicationContract` | Rejects legacy `frontend`/`backend` JSON fields | Generic boundary guard | Keep the fail-closed guard; it prevents the old contract from re-entering the canonical model. |
| `Automaton.Core.Models.AppActionManifest` | Python/Bash Action runtimes and manifest semantics | Actions/controller subsystem | Defer. Actions have separate version/history/execution semantics and are not the generic runtime executor. |
| `RunnerDeploymentService` and `RunnerRuntimeProcessManager` | Structured build/runtime/artifact/readiness/promotion flow | Generic path | Keep stack-neutral. It must not inspect frontend/backend layout or technology names. |
| `RunnerWorkspaceService` generic methods | Application source, artifact, logs, cache, and persistent data roots | Generic path | Keep centralized through `RunnerWorkspaceService` and `ManagedPathBoundary`. |
| `RunnerWorkspaceService` legacy methods | `RUNNER_SCRIPTS_DIR`, controller backend, venv, and legacy data roots | Legacy compatibility | Retain only for the deferred controller bridge; keep target cleanup covering both layouts. |
| `RunnerRuntimeGatewayService` | HTTP proxy to the promoted runtime endpoint | Generic path | Keep transparent and independent of framework, language, frontend, or backend concepts. |
| `RunnerHostService`, `TunnelForwarderService`, `HubService` | FastAPI startup, controller tunnel, backend file transfer | Legacy compatibility | Defer removal; these are explicitly separated from the generic runtime gateway. |
| `RunnerBackendEnvironmentService` and Python diagnostics | Python venv/install/detection | Legacy compatibility | Defer with controller compatibility; generic commands do not use this service. |
| `AppActionDeploymentService` / `AppActionExecutionService` | Python/Bash Action preparation and execution | Actions/controller subsystem | Defer; generic package deployment may prepare Actions, but runtime execution is a distinct contract. |
| `AppDeploymentTarget.LegacyControllerRuntimeDeployed` | Persisted completion marker for legacy controller/runtime tunnel behavior | Legacy compatibility | Rename the former ambiguous `RuntimeDeployed` state and keep it out of generic route eligibility. |
| `AppDeploymentTarget.GenericRuntimeDeployed` | Target-local successful generic runtime promotion | Generic path | Remains the sole generic runtime promotion marker used by routes and generic Studio state. |
| `BackendRouteConfigService` | Runner/visibility/tunnel eligibility plus generic promotion marker | Generic path | Must consume only `GenericRuntimeDeployed`; never legacy deployment state or application technology. |
| `AppsControllerExecutionService`, `AppControllerModels` | `DeployFrontend`, `DeployBackend`, frontend/backend metadata, controller tunnel | Studio compatibility | Keep explicitly legacy-labelled until controller compatibility is removed in a later product change. |
| `AppFileStorageService`, `AppStorageMetadata` | Source/runtime frontend/backend files and legacy controller data | Studio/controller compatibility | Retain for existing apps and file-management features; never use as generic runtime availability. |
| `AppFrontendDeployMode`, `IsStaticHosting`, static hosting projections | Legacy Studio/static publication terminology | Studio compatibility / product projection | Keep only where traced consumers still require it; generic deploy and route decisions use the contract and target promotion state. Removing this projection requires a separate UX/API migration. |
| Historical EF migrations | Former `FrontendDeployed`, `BackendDeployed`, and `RuntimeDeployed` column names | Durable migration history | Do not rewrite applied migrations. New schema changes must use explicit legacy names. |

## Architectural rules locked by Stage 8

1. The generic path is `ApplicationContract -> source -> build -> artifact ->
   runtime -> readiness -> promotion -> routing`.
2. Generic code does not detect Python, Node.js, .NET, Vue, FastAPI, static
   hosting, package manifests, or frontend/backend topology.
3. `GenericRuntimeDeployed` is target-local generic promotion state.
   `LegacyControllerRuntimeDeployed` is compatibility state and must not make a
   generic route or preview available.
4. Generic Runner path access is obtained through `RunnerWorkspaceService` and
   `ManagedPathBoundary`; compatibility paths remain in explicitly named
   legacy helpers.
5. Controller/FastAPI, Python Action, and Studio compatibility code remains
   supported but is not a fallback implementation of the generic runtime.

The architecture tests enforce the type and assembly boundaries that can be
checked mechanically. This document intentionally does not claim that every
legacy occurrence is removable: the deferred compatibility surface is still a
real product dependency until its consumers are retired.
