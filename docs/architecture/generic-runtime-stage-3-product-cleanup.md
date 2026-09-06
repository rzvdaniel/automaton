# Generic Runtime Stage 3 — Product and schema cleanup

Stage 3 removes the remaining active split-runtime state from the product
model. Deployment state is now represented by the generic application package,
runtime artifact, target assignment, health state, and `GenericRuntimeDeployed`.
Storage metadata uses one application source/runtime record rather than
frontend and backend runtime records.

The `LegacyControllerRuntimeDeployed` column is removed by the
`Stage3RemoveLegacyRuntimeState` migration. Historical migrations and inventory
documents remain unchanged; they are records of the former schema, not runtime
authority.

The deployment controller is named `AppDeploymentController`. Its package
deployment, operation polling, and managed-runtime readiness endpoints are the
active generic deployment API.

## Deliberately deferred

The remaining Studio split-editor and backend-file compatibility surface still
needs a generic application-file editing contract before it can be removed
safely. The Runner FastAPI sidecar still owns only retained control-plane file,
flow, and media tunnel operations; it is not an application runtime. These are
the next cleanup candidates, not alternate generic runtime authorities.

The remaining public Website marketing language and the Studio visual editor
labels are user-facing cleanup work. They must be migrated together with their
client contracts, not by changing generic deployment state piecemeal.
