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
- connected-flow artifact transfer through `UploadControllerScript` (Stage 2B2);
- Runner `UploadControllerScript` handling and the controller transport;
- the FastAPI controller runner and its `/api/controller` routes;
- legacy backend source/storage and target/app virtual-environment assumptions.

These are not part of canonical Action execution. Stage 2C must delete the
controller/FastAPI execution bridge after connected-flow artifacts have a
canonical owner, then remove its remaining production state and transport.
