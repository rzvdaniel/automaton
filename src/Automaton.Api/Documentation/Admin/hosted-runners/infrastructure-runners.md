---
title: Infrastructure Runner installation and maintenance
order: 25
---

Use this page when adding a new Runner Host or maintaining an existing Infrastructure Runner. Studio provides verified commands and observes the result; maintenance runs locally on the server.

[![Infrastructure Runner operations](./assets/infrastructure-runner-operations.png)](./assets/infrastructure-runner-operations.png)

## Install on a new server

1. Prepare a Linux server with administrator access and install Docker.
2. In Studio, open **Admin → Runners → Runner Hosts**.
3. Select **Install Infrastructure Runner** for the target host and copy the generated installer command.
4. Connect to the server using SSH or its console, then run the copied command with `sudo`.
5. Return to **Admin → Runners → Runner Hosts** and select **Validate** for the host.
6. Correct any reported problems and validate again. When validation succeeds, select **Approve**.

The initial installer may authenticate with either the generated enrollment token or an Infrastructure Runner API key. Enrollment-token installs use the one-time token only to fetch the protected broker bootstrap configuration; the Runner still exchanges that token through the normal enrollment endpoint and persists its durable API key.

Approval enables Hosted Runner scheduling. Confirm that the host is **Online**, **Ready**, successfully validated, and shows the expected image digest. The server is then ready to host managed Runners. For the canonical host onboarding details, see **Add a Runner Host**.

Infrastructure capability profiles are issued by the enrollment flow; the installer and Repair command do not expose an operator capability override. `AUTOMATON_RUNNER_CAPABILITIES` is a generated runtime projection of that profile. When an existing installation reconnects or is repaired, the current canonical Infrastructure profile is written back before advertisement. The API still authorizes privileged host operations only when the connected Runner explicitly reports the capability and meets the required protocol version.

[![Install an Infrastructure Runner on a new server](./assets/infrastructure-runner-installation.png)](./assets/infrastructure-runner-installation.png)

## Validate host health

**Validate** queues a durable, read-only command for the host's stable Infrastructure Runner identity.
The validation dialog initially shows **queued**; use **Refresh** to read the persisted result and logs.
The Runner executes the bundled validation action and sends its terminal result over authenticated HTTPS.
A temporary API interruption retries result delivery without repeating completed validation work.
If Runner interruption leaves execution uncertain, validation fails explicitly; correct the problem and start a new validation.
Approval remains separate and requires successful validation. Existing storage admission checks still apply.
Deploy matching API, Studio and Infrastructure Runner versions before using this command; the replaced SignalR host-script handler is no longer available.

## Repair an existing Infrastructure Runner

Use **Repair** to reinstall or recover the Infrastructure Runner container without changing its identity or configuration. The current repair command reuses the container's installed immutable image; it does not select or deploy a newer image and does not provide update rollback.

It preserves the installation ID, API credential, `runner.json`, `installation-profile.json`, and other persisted configuration. The installer also refreshes the privileged Infrastructure Runner RabbitMQ settings from the API and stores them in protected host-only configuration so the durable command consumer is available after repair. These settings are never shown in Studio or passed to Hosted Runner containers.

1. Open **Admin → Runners → Runner Hosts**.
2. Select **Repair** for the server, review the installed image digest, and select **Copy command**.
3. Connect to the Runner Host and run the copied command with `sudo`.
4. Wait for the installer to finish, then return to Studio and refresh the details.
5. Confirm that the runner reconnected, is **Online** and **Ready**, passed health validation, retained its identity, and still shows the installed digest.

Existing Hosted Runner containers normally continue running. New provisioning and host workflows are temporarily unavailable while the Infrastructure Runner is disconnected. If repair fails, inspect the host and container logs before retrying.

[![Maintain an existing Infrastructure Runner](./assets/infrastructure-runner-maintenance.png)](./assets/infrastructure-runner-maintenance.png)

## Replace and re-enroll

Use **Replace** only when the existing identity or credentials must no longer be retained—for example, when a credential was lost or exposed, the identity is corrupted, or persisted configuration is invalid.

1. Open **Admin → Runners → Runner Hosts**.
2. Select **Replace** for the server, read the warning, and confirm.
3. Copy the new enrollment command shown by Studio.
4. Run it with `sudo` on the server, then select **Validate** in Studio.
5. Select **Approve** after validation succeeds. Approval enables scheduling.

This creates a new installation identity and credentials. Do not use it for normal repair.

## Revoke an Infrastructure Runner

Use **Revoke** when a server is retired, lost, or compromised.

1. Disable Hosted Runner scheduling and confirm that no required Hosted Runners depend on the server.
2. Open **Admin → Runners → Runner Hosts**, select **Revoke**, review the warning, and confirm.

After revocation, the runner cannot reconnect or perform host operations. Revocation is not a repair operation.

## Which operation should I use?

| Situation | Operation |
| --- | --- |
| Install on a new server | Install Infrastructure Runner |
| Reinstall while keeping configuration | Repair |
| Recover a broken container deployment | Repair |
| Replace lost or compromised credentials | Replace |
| Create a new installation identity | Replace |
| Permanently retire or block the installation | Revoke |

## Troubleshooting

### Durable command development probe

The Runner includes `automaton-runner infrastructure-command-probe` for isolated
development checks. It requires `DOTNET_ENVIRONMENT=Development` and is not started
by the normal Runner service. Production trusted Infrastructure Runner operations use the durable command queue.

The probe consumes one RabbitMQ command and acknowledges it only after the API
accepts the terminal Action result. It retrieves command-scoped secret grants and
posts results through the Runner's existing API-key credential. It is not started
by the normal Runner service, and it does not replace the normal durable consumer. Do not run it against a production broker or use it to retry a live host
operation.

### The command fails to download

Confirm HTTPS and DNS access to the Automaton API, copy a fresh command from Studio, and run it again.

### Checksum verification fails

Do not run a downloaded script manually. Delete the copy, copy a fresh verified command from Studio, and retry. If it fails again, verify that Studio and the API are running the current release.

### The Infrastructure Runner remains offline

On the server, check `docker ps -a` and `docker logs automaton-infrastructure-runner`. Confirm the container is running, its health check succeeds, it can reach the API, the persisted configuration directory is mounted, and its credential has not been revoked.

### The runner is online but not ready

Open **Admin → Runners → Runner Hosts**, run **Validate**, correct the reported failures, validate again, and approve the host if required.

### The image digest did not change

Repair intentionally reuses the installed immutable image. Do not assume **Repair** deploys a newer digest; use the separately supported image update process when one is available.

### Scheduling remains disabled

Confirm the runner is Online, Ready, approved, and successfully validated. Check available CPU, memory, and disk capacity.

### Repair fails

Inspect the container and installer logs. Do not repeatedly run Repair without identifying the failure. Use **Replace** only when preserving the existing identity and configuration is no longer appropriate.

## Important cautions

- Always copy the current command from Studio; do not reuse an old installer command.
- Run maintenance commands directly on the intended server.
- Never include API credentials or enrollment keys in logs, screenshots, or support messages.
- **Repair** preserves identity and configuration but reuses the installed image and has no remote rollback.
- **Replace** creates new identity and credentials.
- **Revoke** permanently blocks the current installation credential.
- Studio provides the command and observes the result; it does not remotely replace the Infrastructure Runner.

## Durable Action management

Drain in-flight legacy Action operations and pause new Action submissions during the matching API/Runner rollout; there is no fallback to the retired transport. Release matching API and Runner versions before using the migrated Action management path. Runners require the configured private TLS broker listener, certificate trust and authenticated HTTPS access to the API. Action execution, module/version management, artifact reads and deployment-target cleanup use stable Runner identity; a fresh heartbeat is required for admission, without a management SignalR connection ID.

Cancellation records intent. Check the persisted execution's terminal status before assuming that work stopped. Interrupted or uncertain execution reports `Lost` and is not automatically repeated. Logs/results retain their existing expiry; an expired artifact is unavailable. Trusted measurement-action readiness still completes before an administrator's storage verification is queued. Studio notifications, Runner presence/heartbeat, and supported BYO runtime tunnels continue to use SignalR; management commands and API deployment do not depend on reconnect/drain coordination. After API color cutover, the active API publishes durable tunnel-reconnect commands on each affected Runner's management queue, including when the old color remains running. Connected Runners on the previous color must support protocol version 5 or the deploy script restores the previous API route. Runners finish active tunnel requests when possible, with a 20-second maximum grace; a request can be interrupted at the bound. If publication fails partially, the script restores the old route and queues compensation through the still-running new API for all registered protocol-v5 Runners, including disconnected Runners. Runners retain the latest target until its API color confirms its identity; the retained older API's `WelcomeRunner` identity payload is supported during rollback. Tunnel requests remain blocked while a superseded color is connected and resume after target confirmation. Keep both API colors running if compensation publication fails; the restored older API endpoint is not required.

The API provisions separate execution and management queues for each Runner, with consume-only access to those two queues. Execution has four delivery slots; management has its own channel and delivery slot so queued execution cannot block log reads, module operations or cleanup. Cancellation wakeups use the management queue. Pre-execution cancellation leaves a durable tombstone after result cleanup. The matching API/Runner rollout must include this queue configuration; older single-queue configuration is rejected rather than used as a fallback.

Deployment finalization also synchronizes connected-flow artifacts through the management queue and authenticated HTTPS. Release matching API/Runner versions before deploying applications; the retired managed-artifact hub handler has no fallback. A persisted deployment retry reuses its pinned synchronization operation. A terminal synchronization failure, including `Lost`, fails the deployment and releases its target so an explicit new deployment can proceed. Callback replay acknowledges the failed deployment without repeating synchronization. Host promotion may already have completed; this failure does not roll back the running container. Supported BYO runtime tunnels remain tied to the API instance holding the Runner's SignalR connection; the durable reconnect command moves connected Runners to the new API color without a deployment registration gate.

Deployment notifications acknowledge admission separately from completion. The API's deployment dispatcher resumes persisted operations, including after a restart or a missed notification, without holding the shared command-dispatch consumer while awaiting Runner results. Independent operations use separate service scopes and database claims. `DeploymentDispatch:MaximumConcurrentOperations` sets the per-API limit (default `4`, must be positive); a deployment waiting for its Runner result occupies one slot, and excess work stays persisted until a slot is available. Check the deployment's persisted terminal status before treating a healthy promoted container or an empty queue as success. A callback timeout can record cancellation intent for the pinned artifact operation; recovery must settle that operation and release the target before an explicit new deployment. Preserve the app, data and pending deliveries while investigating; do not purge queues or reset deployment rows to force a retry.
