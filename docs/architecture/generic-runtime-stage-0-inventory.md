# Generic Runtime Refactor: Stage 0 Inventory

**Status:** Stage 0 inventory and boundary baseline
**Date:** 2026-08-23
**Scope:** Inventory only. This document does not implement the generic runtime contract.

## Architectural target

Automaton currently treats an application as a frontend package plus an optional
Python backend. The target architecture treats an application as an opaque
workload described by an explicit execution contract:

```text
source/workspace
    -> explicit build contract
    -> explicit runtime contract
    -> readiness/health endpoint
    -> runner endpoint
    -> Automaton routing and publishing
```

The control plane and runner should own execution infrastructure, isolation,
storage boundaries, process supervision, logs, and routing. The application
should own its language, framework, package manager, source layout, build
system, and HTTP server. Technology detection may remain in optional templates,
examples, or optimization guidance, but must not select infrastructure behavior.

The intended boundary after the refactor is:

- control plane: persist and transport stack-neutral build, runtime, health,
  environment, port, storage, and capability data;
- runner: validate the workspace boundary, execute the declared build/runtime
  commands, capture logs, supervise the process, and report readiness;
- routing: target an opaque healthy runtime endpoint;
- Studio and CLI: collect or emit the generic contract without requiring a
  frontend/backend pair or a particular language/framework.

No backward-compatible Python/Vue contract is part of this target. Existing
durable state should be migrated once into the new model; obsolete fields and
execution paths should not remain as fallbacks.

## Current lifecycle

### 1. App creation and persistence

1. `AppsService.Create` creates `WebApp`, links the owner, assigns a runner,
   initializes file storage, writes a default `source/frontend/index.html`,
   and writes `source/automaton.json`.
2. `AppDeploymentTarget` stores the deployment identity, slug, runner
   assignment, status, and separate `FrontendDeployed`/`BackendDeployed`
   markers. It does not store a build or runtime specification.
3. `AppFileStorageService` persists app data below
   `AppData/apps/{userId}/{appId}` using fixed `source`, `live`, `versions`,
   `frontend`, and `backend` directories. `_metadata.json` also has separate
   frontend/backend availability, source, and runtime fields.
4. `AppVersionService` snapshots and restores source files. Fork/remix and
   archive flows copy source and, for preview, a sanitized live frontend
   projection.

### 2. CLI deploy request

1. `NewAppService` creates an `automaton.json` containing default frontend and
   backend configuration. The defaults assume `cd frontend && npm install &&
   npm run build`, `frontend/dist`, and `backend`.
2. `ValidationService` requires frontend configuration and an `index.html`,
   scans `backend/` for `.py` controllers, and decides whether a runner is
   needed from that scan.
3. `DeployService` runs only `Frontend.Build`, validates the frontend package
   and optional backend package, creates a restore point, waits for the managed
   runtime, and uploads a ZIP through `AutomatonApiClient`.
4. `ZipService.CreateDeployPackageZip` creates a manifest plus
   `source/`, `runtime/frontend/`, `runtime/backend/`, and action content.
   The package layout is explicit, but the runtime portions are still split by
   frontend/backend role.

### 3. API orchestration and persistence

1. `AppsControllerExecutionController` exposes `POST
   api/apps/{appId}/deploy-package` and the Studio deploy endpoints.
2. `AppsControllerExecutionService.DeployPackageAsync` resolves a target and
   connected runner, uploads the package, invokes the SignalR
   `DeployAppPackage` method, marks both frontend/backend deployment state, and
   persists the package's `source/` content back into API storage.
3. Studio deployment has a separate path. It calls
   `SyncSourceFrontendToRuntimeFrontendAsync` and
   `SyncSourceBackendToRuntimeBackendAsync`, then separately deploys backend
   content to runners. `AppDeployCapabilityService` uses frontend build
   markers and the current source layout to decide whether Studio can deploy.
4. `BackendRouteConfigService` and runtime tunnel services refresh routing
   after deployment. Routing metadata is close to stack-neutral, but its
   inputs are currently produced by backend-specific deployment state.

### 4. Runner deployment and startup

1. `HubService` receives `DeployAppPackage` and delegates to
   `RunnerDeploymentService`.
2. `RunnerDeploymentService` extracts and validates the package, snapshots the
   existing target, optionally removes the target roots, copies `source/` and
   `runtime/`, prepares actions, and then inspects
   `runtime/backend/system-requirements.txt` and `runtime/backend/requirements.txt`.
3. The runner installs system packages or Python requirements, creates a venv
   when needed, persists `deployment.json` with `HasBackend` and
   `BackendModule`, and calls either `EnsureRunningAsync` or `StopAsync`.
4. `RunnerBackendProcessManager` scans `deployment.json` on restart, requires
   `runtime/backend/main.py`, selects a Python executable/venv, starts
   `python -m uvicorn <module>`, allocates a port from a fixed range, waits for
   a TCP listener, captures logs, and kills the process tree on stop.
5. `RunnerWorkspaceService` owns the runner filesystem root but exposes
   stack-specific paths for FastAPI scripts, runtime frontend/backend,
   backend, venv, versions, and app data. It also has a legacy
   `RUNNER_SCRIPTS_DIR` layout bridge.
6. `TunnelForwarderService` still contains FastAPI-named client/configuration
   concepts, while the runtime gateway/tunnel path forwards requests to the
   assigned runner endpoint.

### 5. Readiness, routing, and publishing

- Application readiness is currently a TCP port probe in
  `RunnerBackendProcessManager`; there is no application-declared health
  contract.
- Runner infrastructure readiness separately probes the runner-side FastAPI
  service on port 8001.
- Runtime tunnel and backend route configuration use runner endpoint/port
  information and target identity, which is the closest existing generic
  boundary.
- `PublishedAppStorageService` requires deployed `live/frontend/index.html`,
  copies the frontend runtime into a public static host, injects an Automaton
  script, and rejects root-relative assets with a Vite-specific remediation.
  This is a static frontend publication path rather than publication of an
  opaque HTTP runtime.

### 6. Restart, recovery, and deletion

- Runner restart reconstructs backend processes from target-local
  `deployment.json`; venvs and runtime files are expected to remain on the
  runner filesystem.
- Failed runner package deployment restores a target snapshot containing
  source/runtime roots.
- Target deletion stops the process and deletes the entire target root.
- App deletion claims and cleans all deployment targets, deletes the API-side
  app directory, and then deletes the database entity.
- Reassigning a deployed target requires explicit runtime cleanup.
- Version restore changes API-side source but does not itself define a generic
  rebuild/redeploy operation.

## Coupling inventory

| Area | Current coupling | Classification | Stage 0 disposition |
| --- | --- | --- | --- |
| CLI manifest | `AutomatonConfig.Frontend` / `Backend`, frontend build, deploy directories | Studio/CLI coupling | Replace with one explicit generic application contract in Stage 1/6 |
| CLI validation | `index.html`, `backend/*.py`, frontend/backend presence | Studio/CLI coupling | Delete stack validation; retain path/security validation |
| CLI packaging | `runtime/frontend`, `runtime/backend`, frontend/backend include/exclude rules | Studio/CLI coupling | Replace with generic workspace/artifact packaging |
| API deployment state | `FrontendDeployed` and `BackendDeployed` flags | Persistence/schema coupling | Replace with generic deployment/runtime state after callers are mapped |
| API file storage | `source/frontend`, `source/backend`, `live/frontend`, `live/backend`, frontend/backend metadata | Persistence/schema coupling | Migrate durable source and data; remove enforced role split |
| API Studio deploy | separate frontend sync and backend runner deployment | Control-plane coupling | Converge on the generic deployment path |
| API capability checks | build-marker and source-layout detection | Control-plane coupling | Remove from infrastructure decisions; templates may provide explicit config |
| Runner package deploy | requirements files, system requirements, `HasBackend`, `BackendModule` | Runner coupling | Replace with generic build/runtime commands |
| Runner process start | `main.py`, Python, venv, uvicorn, fixed backend port range | Runner coupling | Replace with opaque executable/process supervision |
| Runner workspace | FastAPI scripts root, backend/venv helpers, legacy scripts layout | Runner coupling | Keep containment/storage ownership; remove technology names and role assumptions |
| Runner sidecar/tunnel | `fastapi_server`, FastAPI-named forwarding fields and health probe | Runner coupling | Separate runner control-plane service from app runtime; genericize naming where it is part of app execution |
| Publishing | deployed frontend `index.html`, static copy, Vite base guidance | Control-plane coupling | Publish an opaque runtime endpoint or make static hosting an implementation behind the same contract |
| Studio UI | frontend/backend deploy buttons, status sections, types, and service calls | Studio/CLI coupling | Rework after the canonical contract exists |
| Templates/examples | default HTML/Vue/Node/Python examples and AI guidance | Template/example-only | May remain if they emit the generic contract and do not alter infrastructure behavior |
| Product UI implementation | Vue/Vite used to build Studio itself | Documentation/template-only for this refactor | Not application-runtime coupling; do not remove |
| Historical guidance/docs | AGENTS, llms, README, and deployment docs describing current app shape | Documentation-only | Update when the contract changes; no runtime behavior is inferred from docs |

## Concrete replacement/deletion list

The following are the primary Stage 1–8 migration candidates identified by this
inventory:

- `src/Automaton.Cli/Models/AutomatonConfig.cs`, especially
  `FrontendConfig`/`BackendConfig`.
- `src/Automaton.Cli/Services/NewAppService.cs`, `ValidationService.cs`,
  `ZipService.cs`, and `DeployService.cs`.
- `src/Automaton.Api/Services/AppDeployCapabilityService.cs`, the split Studio
  deploy methods in `AppsControllerExecutionService.cs`, and the split
  frontend/backend deployment DTOs and UI calls.
- `src/Automaton.Infrastructure/Entities/AppDeploymentTarget.cs` deployment
  booleans and related summaries/projections.
- `src/Automaton.Api/Services/AppFileStorageService.cs` fixed frontend/backend
  roots, build-marker detection, controller helpers, and split runtime
  metadata.
- `src/Automaton.Runner/Services/RunnerDeploymentService.cs`,
  `RunnerBackendProcessManager.cs`, `RunnerBackendEnvironmentService.cs`,
  `PythonEnvironmentDiagnostics.cs`, and the stack-specific portions of
  `RunnerWorkspaceService.cs`.
- `src/Automaton.Runner/fastapi_server/`, FastAPI-specific runner startup, and
  FastAPI-named tunnel forwarding concepts where they describe application
  execution rather than runner control-plane infrastructure.
- `src/Automaton.Api/Services/PublishedAppStorageService.cs` static frontend
  assumptions and Vite-specific validation, once generic runtime publication
  is available.

These are candidates, not edits performed by Stage 0. Historical EF migration
files may remain according to the repository migration policy; runtime callers
must not retain compatibility behavior solely to read obsolete fields.

## Durable state versus disposable state

The one-way migration must preserve or transform these durable identities and
contents:

- application identity, ownership, project/organization membership, target
  identity, slug/domain identity, publication/moderation state, and audit
  history;
- editable application source, `automaton.json` link metadata, arbitrary source
  subdirectories, version snapshots, and user-created application data/media;
- application secrets and runner credentials through the existing secret
  storage/redaction rules;
- action definitions, action versions, execution history, and deployment
  history where they are part of the product record.

The following should be treated as rebuildable or disposable runtime state
unless a later design proves otherwise:

- venvs, package-manager caches, build caches, temporary extraction roots,
  runtime process state, and transient logs;
- generated `live` frontend/backend output when it can be reconstructed from
  source plus an immutable build/runtime definition;
- runner-local process registries and allocated ports.

The current implementation mixes these categories in target roots and API-side
`live` directories. Stage 4 and later stages must make the disposition explicit
before deleting or replacing any data.

## Stage 0 baseline tests

The current baseline is intentionally stack-specific and should remain green
until each later stage replaces the behavior deliberately:

- `Automaton.Cli.Tests/Services/DeployServiceTests.cs` covers build failure,
  managed-runtime readiness, package upload/deploy result shape, and named
  deployment targets.
- `Automaton.Runner.Tests/RunnerDeploymentTargetIsolationTests.cs` covers
  package extraction into target-scoped source/runtime roots, deployment
  metadata, process stop behavior, and target cleanup isolation. It also records
  the current `HasBackend`/`BackendModule` marker used to restart Python
  backends.
- `Automaton.Api.Tests/AppFileStorageServiceTests.cs` covers source/runtime
  separation, version restore, fork-safe copying, and current frontend build
  marker detection.
- `Automaton.Api.Tests/PublishedAppStorageServiceTests.cs` covers static
  publication, path safety, and current portable-index/Vite-base validation.
- `Automaton.Api.Tests/RuntimeTunnelRoutingTests.cs` covers runtime-tunnel
  route precedence relative to published-app routing.

These tests are a migration baseline, not an approval of the old contract.
Later stages should replace assertions that encode technology or role
assumptions with assertions over the generic contract.

## Stage 0 boundary and exit criteria

Stage 0 is complete when this inventory is reviewed alongside the referenced
implementations and establishes:

1. the current end-to-end lifecycle and its split execution paths;
2. the exact control-plane, persistence, runner, Studio/CLI, template, and
   documentation coupling categories;
3. the concrete replacement/deletion candidates;
4. durable state that must survive a one-way migration; and
5. a test baseline that later stages can intentionally replace.

No generic runtime types, compatibility DTOs, fallback parsers, database
migrations, or execution-path changes are introduced by Stage 0.
