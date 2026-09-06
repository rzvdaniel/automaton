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

## Stage 3 boundary and remaining compatibility

The active application contract is generic: Studio exposes one application
source/runtime deployment capability, source archives use `/source/upload`,
and the old backend upload and Runner backend-file APIs are removed. User-owned
directories named `frontend` or `backend` remain ordinary source content; they
are not platform runtime concepts.

The Runner FastAPI process is retained only as a bounded control-plane adapter
while its callers are migrated. Its exact remaining routes are:

- `POST /api/runtime-file`, consumed by `RuntimeFileProxyService` for the
  published-file compatibility path;
- `POST /internal/apps/{appId}/targets/{targetId}/api/flows/{flow}/execute`,
  consumed by the tunnel forwarder and delegating to the .NET flow command;
- `POST /internal/apps/{appId}/targets/{targetId}/api/files/upload` and
  `GET /internal/apps/{appId}/targets/{targetId}/api/files/{session}`, used by
  the application file-transfer compatibility path;
- the catch-all internal API route, which returns 404 and executes no
  application code; `/health`, `/shutdown`, and diagnostics are process
  lifecycle endpoints.

These routes do not start or host user application processes and do not use the
legacy controller runtime. The bounded follow-up is to move the file-transfer
compatibility path to the existing .NET Runner/SignalR contract, then remove
the Python sidecar and its packaging/entrypoint. No new product endpoint may
depend on the sidecar.

Historical migrations, inventories, and user-owned source names may retain
legacy terminology. Active API, CLI, Studio, and public guidance must use the
generic application/runtime vocabulary.
