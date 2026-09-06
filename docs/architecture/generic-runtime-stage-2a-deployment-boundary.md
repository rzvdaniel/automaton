# Generic Runtime Stage 2A: deployment boundary

Stage 2A makes generic package deployment authoritative without attempting the
later Action/controller migration. The production package path is:

```text
DeploymentOperationService
  -> ApplicationDeploymentService
  -> package contract validation and upload
  -> Runner DeployAppPackage
  -> ApplicationContract build/runtime/readiness/promotion
  -> GenericRuntimeDeployed
```

`ApplicationDeploymentService` owns target and connected-Runner resolution,
package transfer, generic deployment-state fencing, contract persistence, and
generic route reconciliation. It does not inspect frontend/backend layout,
upload controller scripts, run controller tunnels, or consult
`LegacyControllerRuntimeDeployed`. A successful package deployment is
authoritative only when the generic Runner promotion and API-side finalization
complete.

## Deliberately remaining compatibility consumers

`AppsControllerExecutionService` remains registered only for the existing
backend/controller compatibility endpoints and connected-flow artifact path.
The controller-action endpoints are now an explicit retired boundary and no
longer execute Actions. `UserAppsController` still uses restricted
compatibility helpers. Runner `HubService`, `fastapi_server`, and the legacy
workspace/dependency helpers remain for that bridge. Product-level Automaton
Action semantics and the separate Action module executor are preserved by
Stage 2B.

No new generic deployment caller may depend on these compatibility services.
Stage 2C may remove the controller/FastAPI bridge and its legacy runtime state
after connected-flow artifacts and the remaining backend consumers are gone.
This sequencing does not make the bridge a second generic runtime architecture
or a fallback for package deployment or Action execution.
