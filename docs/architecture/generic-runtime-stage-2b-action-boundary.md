# Generic Runtime Stage 2B: Action execution boundary

Stage 2B keeps Automaton Actions as a product capability while removing their
execution from the legacy controller runtime.

## Canonical Action execution

`AppActionExecutionService` is the execution boundary for declared Actions. It
owns authorization, runner and deployment-target resolution, idempotent
dispatch, scheduled execution, execution history and state, inputs and
results, logs, artifacts, and the configured limits/timeouts. Its callers use
the canonical `/api/apps/{appId}/actions/...` API and the existing scheduling
handlers.

Action execution is a finite-work contract. It is not implemented by sending a
request to an application's generic HTTP runtime.

## Retired controller-action boundary

The old published, preview, and private controller-action routes now return
`410 Gone` with a migration message. They no longer call
`AppsControllerExecutionService`, upload controller scripts, auto-deploy a
controller, or tunnel through `/api/controller`. There is no fallback from a
canonical Action failure to the controller path.

The routes and `AppControllerAction*` models remain as an explicit compatibility
surface until Stage 2C removes them. Their old anonymous/mock/controller-name
semantics have no reliable mapping to the canonical Action key.

## Remaining legacy consumers

The following controller-era surfaces remain intentionally isolated for the
next migration slices:

- legacy backend deployment and backend runtime file compatibility endpoints;
- Runner `UploadControllerScript` handling and the controller transport;
- the FastAPI controller runner and its `/api/controller` routes;
- legacy backend source/storage and target/app virtual-environment assumptions.

Connected flows are no longer in this list: Stage 2B2 owns them through the
generic managed-artifact synchronization boundary described below. These
remaining consumers are not part of canonical Action execution. Stage 2C must
delete the controller/FastAPI execution bridge and its remaining production
state and transport.

## Connected-flow artifact boundary

`ConnectedFlowArtifactService` reads the owner-authorized `ConnectedFlowIds`,
serializes each flow plus `index.json`, and sends a complete artifact set to
the assigned target through `SynchronizeApplicationManagedArtifacts`. Runner
stages the set and atomically replaces the target's
`artifacts/current/.automaton/flows` directory. This preserves updates and
removals, target isolation, and path containment without controller names,
backend paths, or application execution semantics. `AppFlowExecutionService`
reads the same generic artifact root.

`UploadControllerScript` remains only for these concrete legacy consumers:

- `AppsControllerExecutionService.DeployBackendToRunnersAsync`, which transfers
  legacy backend files;
- `Runner.HubService.UploadControllerScriptHandler`, which receives that legacy
  controller/backend payload;
- the remaining FastAPI controller routes and
  `fastapi_server/controller_runner.py` compatibility path.

Stage 2C may remove the transport after those consumers are migrated;
connected-flow behavior must not be moved back to it.
