# Runner management transports

Updated: 3 October 2026.

This guide describes the Runner management transport after the Slice 4 changes in review. Slice 4 release and live transfer acceptance are pending. Cross-host package/artifact availability has not been established.

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

SignalR remains for independent Studio updates and supported BYO runtime tunnels. Runner registration/heartbeat, welcome/ping and reconnect-when-idle coordination also remain. Slice 5 will remove replaceable connection/drain coordination and API-slot registration gates after auditing the remaining dependencies; it must retain supported Studio and BYO behavior. The current guide does not claim that Slice 4 has been released or live-accepted.
