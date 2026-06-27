# Managed Runner Workspace Migration Plan

Date: 2026-06-27

## Summary

This plan keeps the core architectural decision from the earlier refactor proposal:

- The runner owns the durable app workspace.
- The API becomes the control plane.

This version narrows scope so the migration can ship incrementally without trying to replace deploy packaging, app serving, backend lifecycle management, Studio file browsing, and terminology all at once.

Companion plan:

- `docs/plans/runner-runtime-access-simplification-plan.md`

Use this workspace migration plan as the storage/orchestration backbone.
Use the companion plan as the runtime-access and host-boundary follow-up once runner-owned deploy/runtime state is real.

## Core Decision

Target model:

- API owns metadata, auth, permissions, runner assignment, publish state, deployment records, version metadata, and orchestration.
- Runner owns app files, runtime files, app data, logs, versions, Python environments, and backend process state.

Target workspace shape:

```text
/automaton/workspace/
├─ apps/
│  └─ {appId}/
│     ├─ source/
│     │  ├─ frontend/
│     │  └─ backend/
│     ├─ runtime/
│     │  ├─ frontend/
│     │  └─ backend/
│     ├─ versions/
│     ├─ logs/
│     └─ app.json
├─ data/
│  └─ {appId}/
├─ cache/
└─ runner/
```

Important constraint:

- The container image is disposable.
- The mounted workspace is durable.

## What To Keep

- One managed runner container per user.
- Multiple apps per managed runner for the same user.
- One persistent workspace volume per managed runner.
- API-mediated private preview at first.
- CLI local build behavior for frontend apps.
- Central runner path resolution through one service.

## What To Defer

Do not try to land these in the same wave as the workspace cutover:

- Full runner HTTP gateway
- Per-app backend process manager
- Broad runner-backed filesystem APIs for every scope
- `live` to `runtime` rename across the whole platform
- Full Studio file/data/log/version refactor
- Public NGINX routing directly into runner gateway

These are valid later phases, but they should not block the workspace migration.

## Non-Goals For The First Shipping Milestone

- No full elimination of current API-side app flows in the first step.
- No attempt to redesign public app serving immediately.
- No attempt to replace every old endpoint in one PR.
- No attempt to introduce strong per-app isolation inside one user runner.

## Implementation Strategy

### Phase 1 - Runner Workspace Foundation

Goal:

- Make the runner workspace real and consistent for both local and hosted runners.

Scope:

- Introduce `AUTOMATON_WORKSPACE_ROOT`.
- Centralize runner app/data/backend/media path logic in `RunnerWorkspaceService`.
- Store app backend files under workspace-backed paths.
- Store per-app data under `/automaton/workspace/data/{appId}`.
- Keep legacy `RUNNER_SCRIPTS_DIR` compatibility only as a bridge where necessary.
- Align hosted-runner Docker provisioning to mount the durable volume at `/automaton/workspace`.
- Update runner docs and tests for the new layout.

Exit criteria:

- Hosted and local runners resolve the same effective workspace model.
- No managed runner still depends on `/app/data` as the primary mounted app filesystem.
- Runner path validation remains centralized and test-covered.

Verification:

- `dotnet test src/UnitTests/Automaton.Runner.Tests/Automaton.Runner.Tests.csproj`
- `dotnet test src/UnitTests/Automaton.Api.Tests/Automaton.Api.Tests.csproj --filter HostedRunner`

### Phase 2 - Single Deploy Package

Goal:

- Replace split deploy uploads with one package that the API forwards to the assigned runner.

Scope:

- Add a deployment package format:

```text
package.zip
├─ manifest.json
├─ source/
│  ├─ frontend/
│  └─ backend/
└─ runtime/
   ├─ frontend/
   └─ backend/
```

- Add runner command: `DeployAppPackage`.
- Add `RunnerDeploymentService`.
- Extract package contents into runner workspace.
- Create a version snapshot before overwriting deployed app files.
- Report deployment success or failure back through the API.

Keep the manifest minimal at first:

- `appId`
- `deploymentId`
- `createdUtc`
- whether frontend/backend source/runtime are present

Do not over-design the manifest before the runner needs more fields.

Exit criteria:

- One CLI package upload replaces source upload, frontend runtime upload, backend runtime upload, and separate backend deploy orchestration.
- Deployed app source and runtime are stored together in the runner workspace.
- Deployment failures do not leave path traversal or partial extraction bugs.

Verification:

- Add targeted deploy packaging tests in CLI and runner test projects.
- Add runner extraction and snapshot regression tests.

### Phase 3 - API Deploy Orchestration Cutover

Goal:

- Move deploy orchestration into API control-plane logic without API-local durable app storage remaining the source of truth.

Scope:

- Add `POST /api/apps/{appId}/deploy-package`.
- Validate app ownership and runner assignment.
- Forward the package to the assigned runner.
- Record deployment metadata in the database.
- Keep old endpoints temporarily only if needed to avoid a broken CLI transition.

Exit criteria:

- New deploy path is API -> assigned runner.
- API-local app filesystem is no longer the primary deployed state for migrated apps.
- Deployment records remain queryable from the API.

Verification:

- Targeted API tests for runner assignment, authorization, and failure handling.

### Phase 4 - Runner-Backed Versions

Goal:

- Make snapshots and restores operate against runner-owned app state.

Scope:

- Version snapshots live physically in the runner workspace.
- API stores version metadata only.
- Restore supports full source + runtime restore.
- Restart backend after restore only when runtime/backend changes require it.

Important rule:

- Be explicit about restore semantics.
- Do not silently treat source-only and full runtime restore as the same operation.

Exit criteria:

- Versions are no longer tied to API-local app storage.
- Restore behavior is predictable and tested.

Verification:

- Snapshot-before-overwrite tests
- Full restore tests
- Backend restart decision tests

### Phase 5 - Studio Source File Cutover

Goal:

- Move editable source browsing and editing to the assigned runner first.

Scope:

- Add API orchestration endpoints for source file tree, read, write, and delete.
- Keep scope limited to editable source first.
- Delay broad runtime/data/log mutation APIs.

Reason:

- Source editing is a core product flow.
- Runtime/data/log access is operational and can follow after deploy and version flows are stable.

Exit criteria:

- Studio source file operations talk to the assigned runner rather than API-local disk.

Verification:

- Relevant backend tests
- `cd src/automaton-studio-vue && npm run type-check && npm run build`

### Phase 6 - Backend Lifecycle Manager

Goal:

- Replace ad hoc backend lifecycle behavior with an explicit runner-side app backend manager.

Scope:

- Add `AppBackendProcessManager`.
- Track app backend port, status, and process state.
- Add start, stop, restart, and health-check behavior.
- Store durable desired state in narrow per-app metadata only.

Important constraint:

- `app.json` must stay narrow.
- Do not turn it into a second database or an unbounded state dump.

Exit criteria:

- Backend process state is managed per app rather than indirectly through a global process assumption.

Verification:

- Targeted runner process manager tests
- Restart and health-check tests

### Phase 7 - Runner Gateway

Goal:

- Serve runtime frontend files and proxy app backend traffic from the runner.

Scope:

- Add runner web host or extend existing runner host.
- Serve static files only from `runtime/frontend`.
- Add SPA fallback for HTML requests.
- Proxy `/api/*` to the app backend process.
- Keep all non-public directories blocked.

Public serving policy:

- Allow only `runtime/frontend`.
- Never serve `source`, `runtime/backend`, `versions`, `logs`, `data`, `cache`, `runner`, or `app.json`.

Exit criteria:

- Runner can serve one app’s runtime frontend and backend API safely.

Verification:

- Static allowlist tests
- SPA fallback tests
- Backend proxy tests

Runtime-access alignment:

- This phase is the infrastructure foundation for Phase 3E in `runner-runtime-access-simplification-plan.md`.
- Do not treat the gateway as only a transport detail.
- It is the start of moving app runtime delivery away from API-hosted special cases and toward a dedicated runtime surface.

### Phase 8 - Managed Hosting Edge Routing

Goal:

- Route public managed apps from host NGINX to the appropriate runner gateway.

Scope:

- Expose runner gateway only on localhost or private Docker network.
- Generate NGINX routing for published apps.
- Keep private previews API-mediated or tunnel-mediated until auth is settled.

Exit criteria:

- Published managed apps can route through NGINX to runner gateway.
- Private previews are not accidentally exposed.

Verification:

- NGINX config generation tests or syntax checks where applicable
- Publish routing smoke validation

Runtime-access alignment:

- This phase should be implemented together with the explicit runtime-host model from Phase 3F in `runner-runtime-access-simplification-plan.md`.
- Avoid adding more API-versus-Studio host inference once runtime-host configuration exists.

### Phase 9 - Terminology Cleanup

Goal:

- Rename `live` to `runtime` across code, docs, and UI only after the storage and orchestration model is stable.

Reason:

- This is a large clarity improvement.
- It is also an easy way to create broad churn and mixed-contract bugs if done too early.

Exit criteria:

- Old terminology is removed in one deliberate cleanup phase rather than leaking across partial migrations.

## Design Guardrails

### 1. One Path Service

- All runner path construction must go through `RunnerWorkspaceService`.
- Do not scatter `Path.Combine(...appId...)` logic across services.

### 2. API Is Control Plane, Not File Plane

- The API may cache metadata.
- The API should not remain a shadow durable app filesystem after cutover.

### 3. Source And Runtime Stay Distinct

- Editable files live under `source`.
- Deployed runnable files live under `runtime`.
- Do not blur those responsibilities during restore, pull, deploy, or Studio editing flows.

### 4. Public Serving Must Stay Narrow

- Public traffic may only reach runtime frontend assets and explicitly proxied backend routes.
- Never expose internal filesystem scopes.

### 5. Same-User Multi-App Runner Is Not Isolation

- This is an operational simplification and cost optimization.
- It is not strong security isolation between apps.

## Risks

### Migration Scope Risk

The biggest risk is trying to change deploy, storage, public serving, Studio filesystem APIs, and backend lifecycle in one cut.

Mitigation:

- Land phases separately.
- Keep verification targeted and incremental.

### Contract Drift Risk

Old API-local storage assumptions likely exist in more places than the obvious file services.

Mitigation:

- Cut over one user-facing flow at a time.
- Add regression tests around deploy, restore, and source/runtime separation.

### Backend Lifecycle Risk

Per-app backend process management is real infrastructure work.

Mitigation:

- Treat it as a dedicated phase, not a side effect of workspace migration.

### Terminology Risk

Partial `live` and `runtime` mixing will create confusion.

Mitigation:

- Delay the global rename until the architecture is already stable.

## Recommended First Milestone

The first milestone that should be considered shippable is:

- Phase 1 completed
- Phase 2 completed
- Phase 3 completed

That gives Automaton:

- a real runner workspace
- a single deploy package
- API-to-runner deploy orchestration

Without yet taking on:

- public serving rewrite
- per-app backend process manager
- full Studio filesystem cutover
- full naming cleanup

## Phase Mapping To Runtime Access Simplification

These two plans are meant to stack, not compete.

Recommended sequence:

1. Complete this plan’s Phase 1 through Phase 3.
2. Use those phases to make runner-owned deploy/runtime state the source of truth.
3. Then execute the runtime simplification plan in this order:
   - Phase 3A - define runtime boundaries
   - Phase 3B - unify private/public runtime serving
   - Phase 3C - move runtime frontend ownership to the runner
   - Phase 3D - reduce private/public to access policy
4. Continue with this plan’s Phase 4 through Phase 6 as runner-owned versions, Studio source cutover, and backend lifecycle mature.
5. Land this plan’s Phase 7 together with the runtime simplification plan’s Phase 3E.
6. Land this plan’s Phase 8 together with the runtime simplification plan’s Phase 3F.
7. Finish with this plan’s Phase 9 terminology cleanup.

Practical reading:

- This plan answers: where app state lives, how deploy works, and which system owns runtime state.
- `runner-runtime-access-simplification-plan.md` answers: how apps should be opened, routed, served, and separated from Studio once runner ownership exists.

Dependency rule:

- Do not try to fully simplify runtime routing before runner-owned deploy/runtime state is real.
- Do not keep expanding API-hosted runtime special cases after Phase 3 is complete.

## Immediate Next Steps

1. Finish any remaining workspace-path cleanup in runner code.
2. Add deploy-package contract types and runner command shape.
3. Implement `RunnerDeploymentService`.
4. Update CLI deploy packaging to build one package.
5. Add API deploy-package orchestration endpoint.
6. Add regression tests before removing old deploy endpoints.
7. After Phase 3 is stable, begin the runtime-access simplification plan instead of adding more special-case app routes.

## Files Likely Affected By The Next Milestone

Runner:

- `src/Automaton.Runner/Services/HubService.cs`
- `src/Automaton.Runner/Services/RunnerWorkspaceService.cs`
- `src/Automaton.Runner/Services/RunnerDeploymentService.cs`
- `src/Automaton.Runner/Dockerfile`
- `src/Automaton.Runner/docker-entrypoint.sh`

API:

- `src/Automaton.Api/Controllers/AppsController.cs`
- `src/Automaton.Api/Services/AppFileStorageService.cs`
- `src/Automaton.Api/Services/HostedRunnerProvisioningService.cs`
- runner orchestration services and app deploy services

CLI:

- `src/Automaton.Cli/Commands/DeployCommand.cs`
- `src/Automaton.Cli/Services/DeployService.cs`
- `src/Automaton.Cli/Services/AutomatonApiClient.cs`
- `src/Automaton.Cli/Services/ZipService.cs`

Tests:

- `src/UnitTests/Automaton.Runner.Tests/**`
- `src/UnitTests/Automaton.Api.Tests/**`
- `src/UnitTests/Automaton.Cli.Tests/**`

## Final Recommendation

Proceed with the migration.

Do it in a narrower order than originally proposed:

- workspace first
- deploy package second
- API deploy cutover third
- versions fourth
- Studio source editing fifth
- backend lifecycle sixth
- runner gateway seventh
- managed hosting edge routing eighth
- terminology cleanup last

That preserves the architecture goal while sharply reducing migration risk.

After Phase 3, treat runtime-access simplification as the next architecture step rather than a separate optional cleanup.
