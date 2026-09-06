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

## Validation record

The generic `ApplicationDeploymentServiceTests` fixture is retained under its
current name and continues to cover operation persistence, execution leases,
package/source validation, generic runtime promotion, route reconciliation,
failure compensation, and fencing. Legacy-marker-only inputs and assertions
were removed.

`GenericRuntimeStage2CArchitectureTests` checks that the deleted controller
service is not loadable and scans production execution sources for the deleted
controller transport and Python execution boundary. It separately scans
runtime decision services/controllers/hubs for the legacy deployment marker.

The API and Runner projects build successfully. Focused API tests pass for the
restored generic deployment fixture and the generic deployment/action/connected-
flow/Stage 2C guardrail group. The broader API action/authorization filter
passes 116 of 117 tests. The one remaining failure,
`GetPublishedAppInfoAsync_UsesProjectOwnerAsOwnerId`, also fails on the
Stage 2C base commit `8fb54788d452ee30973b2c8864de7b473730e919` and is
pre-existing. The legacy-marker authorization expectations were updated to
prove that the inert marker does not block reassignment or publishing.

Runner suites were run independently for generic runtime/deployment, process
management, Actions, connected flows, workspace handling, Hub/tunnel behavior,
and the readiness regression; all passed. The full Runner project also
completed successfully: 110 passed, 8 skipped, 0 failed in 1m56s. The Worker
Release build also succeeds.
