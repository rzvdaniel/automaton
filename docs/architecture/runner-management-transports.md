# Runner management transports

Updated: 4 October 2026.

This guide describes the Runner management transport after Slice 4 was released as `e71ca380` and the Slice 5 connection-cleanup change was prepared on `feat/rm-slice5-connection-cleanup` (not yet reviewed or released). The 2026-10-04 same-host baseline verified published-media byte integrity and cleanup, an active media read across API color switch, workflow/custom-step results, and automatic host-validation recovery. Cross-host package/artifact availability remains unverified.

## Durable operation path

```mermaid
flowchart LR
    Client[CLI or Studio request] --> API[API caller and existing authorization]
    API --> GW[RunnerFileTransferGateway]
    GW --> Store[Shared API AppData artifact store]
    GW --> DB[(Protected RunnerExecutionOperation)]
    DB --> Outbox[Transactional CAP event]
    Outbox --> Queue{RabbitMQ queue}
    Queue --> Runner[Runner execution consumer]
    Runner --> Assign[Authenticated HTTPS assignment]
    Runner --> Input[HTTPS input artifact, when needed]
    Runner --> Work[Runner journal and file/deploy action]
    Work --> Output[HTTPS output artifact, when needed]
    Work --> Result[Authenticated HTTPS terminal result]
    Result --> DB
    DB --> GW
    GW --> API
    API --> Client
```

RabbitMQ messages contain only operation and Runner identities. The executable request is protected at rest by the existing API Data Protection service. API replicas that serve a given operation must share its existing key ring and, for artifacts, the existing package volume. Runner delivery is journaled: a saved result is returned on redelivery, so completed work does not run again after a lost callback or restart. Accepted work whose outcome cannot be known is terminalized as `Lost`, rather than repeated.

`RunnerExecutionOperationService` routes media stats/metadata to the separate management queue. File writes, downloads, uploads and deployment work use the execution queue. The Runner processes them through `RunnerExecutionProcessor` and `RunnerFileTransferExecutor`; completed results are applied through the same authenticated HTTPS result endpoint as other durable Runner operations.

## File-transfer operations

| Operation | API caller | Queue | Runner behavior | Artifact direction |
| --- | --- | --- | --- | --- |
| App file upload | `AppFileUploadService` | Execution | Fetches bytes into the existing `Path.GetTempPath()/uploads/{session}` area | API store → Runner HTTPS GET |
| App file download | `AppFileDownloadService` | Execution | Reads the existing `Path.GetTempPath()/results/{session}` file | Runner HTTPS POST → API store → API response |
| Media upload | `AppMediaStorageService` | Execution | Writes into the app's existing data/media root | API store → Runner HTTPS GET |
| Media download | `AppMediaStorageService` | Execution | Reads the app media file after metadata lookup | Runner HTTPS POST → API store → API response |
| Media stats/metadata | `AppMediaStorageService` | Management | Reads current app media usage or file metadata | No artifact |
| Generic deployment fallback | `DeploymentPackageRunnerService` | Execution | Fetches the package and calls `RunnerDeploymentService` | API store → Runner HTTPS GET |

The Hosted Runner's existing package-deployment RabbitMQ/HTTPS path remains separate. Slice 4 migrates the generic Runner fallback and app file/media transfers; it does not alter Hosted Runner command dispatch.

## Authorization, bounds and storage

- App-facing API routes retain their existing authentication and app ownership checks. Before dispatch, `RunnerFileTransferGateway` verifies that the requested Runner remains assigned to a target owned by the requesting account.
- Artifact endpoints require the Runner API key and stable `X-Runner-Id`; the API authorizes that key against the persisted operation's Runner. Artifact reads/writes are refused for cancelled or terminal operations.
- Each transfer artifact is capped at 200 MiB. The existing `DeploymentPackageStore` capacity applies to the combined package and transfer files; defaults are 1 GiB and 100 files. Writes and expiry cleanup take the existing AppData lock file so API processes sharing that volume serialize capacity checks. No separate storage service or broker payload is introduced.
- Runner input requests pin their length and SHA-256 in the protected operation. Download results include the output length and SHA-256; the API checks both against the stored artifact before accepting a successful result.
- Inputs are deleted after a terminal result. Download outputs are deleted under the shared AppData storage lock after the API receives the persisted terminal result and finishes streaming them, so ordinary media reads do not accumulate files; abandoned outputs remain covered by expiry cleanup. Incomplete operations retain required inputs and outputs so retry/recovery can proceed.
- Each app-file download HTTP attempt gets a fresh operation ID, while the requested file session remains in the command. A prior `404`, or a successful operation whose output was later cleaned up, cannot poison a later retry using the same session.
- Blue/green API slots on the same host share `AppData/deployment-operations`. Availability when slots run on different hosts is **not established**. Do not treat same-host slot acceptance as proof of cross-host storage.

## SignalR after Slice 4

The file chunk registries, chunk DTOs, API chunk callbacks, `HubFileTransferService`, and `DeployAppPackage` Runner hub handler are removed. Workflow/custom-step execution, App Actions, managed-artifact synchronization, trusted host-container operations, host validation and storage telemetry use their migrated durable/API paths.

SignalR remains for independent Studio updates, Runner registration/heartbeat/presence, and supported BYO runtime tunnels. Welcome, ping, Runner drain/migration endpoints, and API deployment registration gates are removed by the pending Slice 5 change. After API traffic switches, the redeploy script asks the active API to publish durable tunnel-reconnect commands on the existing Runner management queues for Runners still recorded on the previous API color. This works when `--keep-previous` retains an older API image and with multiple API replicas per color. Connected Runners must support protocol version 5; if any cannot accept the command, the deploy script restores the previous API route. The Runner stops accepting new tunnel requests, lets current requests finish when possible, and reconnects immediately when idle or after a 20-second maximum grace. The deployment does not wait for Runner registration; management commands, results and file transfers continue through RabbitMQ and authenticated HTTPS without a SignalR callback.

Runtime tunnels remain instance-local: the API validates that the Runner's recorded API instance matches the serving API and relays through that instance's SignalR connection. The bounded tunnel-only command also runs with `--keep-previous`, so leaving the prior API alive does not leave Runners attached to it indefinitely. Runners already connected to any replica of the active API color ignore duplicate reconnect commands. Tunnel requests can return 503 during the reconnect handoff, and a request still active at the 20-second bound may be interrupted. Failure to publish reconnect commands causes the deployment script to restore the prior API route. This change does not add a cross-slot tunnel relay or promise zero tunnel interruption. Studio notifications and supported BYO tunnel behavior remain supported.

## Focused production validation — 2026-10-03

Runner pipeline #4951 passed from `e71ca380` and published `rzvdaniel/automaton-runner@sha256:4eda68e84a005730a15be3370a5b607211145fb6e817ac0baf8a6946195e272a`. The API was deployed to the blue color from the same revision, with the previous green API image retained. The protected Hosted Runner pin was backed up before updating; its reprovisioned container ran the published digest and registered with the new API color. The Infrastructure Runner remained on its previous digest and was not used to execute the file-transfer acceptance.

Using the retained unpublished app, authenticated app-file upload and download both completed through the Hosted Runner. Upload bytes staged by the Runner and download response bytes matched their expected SHA-256 values. A media upload completed and the persisted app-media bytes matched the expected SHA-256; its test file was removed. A supported CLI pull/deploy then uploaded five package files, applied them on the Runner, and returned a terminal successful deployment result without warnings. The app health endpoint returned `200 ready`, and its app-originated persistence marker endpoint returned HTTP 200 after deployment; this run did not capture a pre-deploy marker hash for comparison.

At verification, the active Hosted Runner execution, management and command queues each had one consumer and zero ready/unacknowledged messages. Production `prod-verify.sh` passed. These checks do not close Slice 5 or the combined RM recovery/cutover acceptance.

## Same-host RM baseline — 2026-10-04

Using the explicitly authorized retained test app, a temporary public-unlisted media route returned 2 MiB and 16 MiB files with exact expected SHA-256 values. The larger read was still streaming when the normal API cutover switched blue to green; it completed with the full byte count and matching digest. Transfer output cleanup left no artifacts after response completion, and the temporary route/files were removed. An in-flight package fetch across a slot switch and different-host API slots remain unverified; the two production API slots share the same host and package volume.

Through the authenticated Studio session, a temporary custom step and workflow both completed on the Infrastructure Runner with persisted successful results/logs; they were deleted afterward. Separately, the Infrastructure Runner was paused during host validation and automatically resumed. The persisted operation reached `Succeeded`, with one result log and healthy Docker/network/disk/API checks. No manual restart was used. During the pause the execution queue showed one consumer, one ready delivery, and one unacknowledged delivery; after recovery, infrastructure and execution queues each had one consumer and zero ready/unacknowledged messages. API/Studio/Website health checks passed. These are pre-Slice-5 baselines; repeat the relevant interruption and recovery checks against the released Slice 5 revision during final combined acceptance.
