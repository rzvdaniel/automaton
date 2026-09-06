# Generic Runtime Stage 2C — Legacy Execution Removal

Stage 2C makes the generic application package/runtime path the only
production application-runtime architecture. Applications own their HTTP
process; Runner owns lifecycle, workspace, health, and routing boundaries.

## Removed execution paths

- `AppsControllerExecutionService` and its split frontend/backend deployment
  and controller-action execution methods.
- Runner `UploadControllerScript` transport and its per-application Python
  environment provisioning.
- `fastapi_server/controller_runner.py`, `/api/controller/...`, and the
  internal controller-action tunnel route.
- API route eligibility based on `LegacyControllerRuntimeDeployed`.
- Production writes and runtime decisions based on the legacy deployment
  marker.

Canonical Automaton Actions remain a product feature and continue to use
`AppActionExecutionService`. Connected-flow artifacts remain a separate
product feature and use `SynchronizeApplicationManagedArtifacts` in the
target-scoped generic workspace. Neither is controller execution.

## Remaining compatibility surfaces

The `LegacyControllerRuntimeDeployed` database column remains as inert
historical data for this stage. No production service reads or writes it; the
physical column and its historical migration metadata are Stage 3 cleanup
candidates.

Runner FastAPI remains only as a control-plane compatibility process for
runtime-file, connected-flow, file-transfer, and media operations that have not
yet moved to their final product surfaces. It must not execute application
controllers or serve as an application runtime.

Backend file-management DTOs/endpoints and related legacy workspace helpers
remain for the current Studio compatibility surface. They are not consulted
for generic deployment or runtime routing and are Stage 3 cleanup candidates.

## Stage 3 candidates

- Remove remaining user-facing controller/frontend/backend terminology and
  obsolete CLI, Studio, API, and publishing contracts.
- Remove backend file-management compatibility endpoints and DTOs after their
  replacement surface is available.
- Remove the remaining Runner control-plane FastAPI file/media/flow bridge
  after those consumers have moved to generic boundaries.
- Physically remove the inert legacy deployment column and historical schema
  artifacts.

## Guardrail

New production runtime code must not reference controller execution,
FastAPI/Python application startup, split frontend/backend runtime state, or
`LegacyControllerRuntimeDeployed`. Historical inventories and migration
records may retain those names when documenting what was removed.
