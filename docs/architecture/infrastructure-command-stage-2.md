# Infrastructure command consumption, Stage 2

Stage 2 adds one direct RabbitMQ consumer to the Runner. The only executable entry
point is `automaton-runner infrastructure-command-probe`, which requires
`DOTNET_ENVIRONMENT=Development`. Normal Runner startup does not register or start
the consumer. The API and Worker still have no live publisher for the new topic.

## Transport and identity

`InfrastructureCommandConsumer.ConsumeOneAsync` uses RabbitMQ.Client 7.2.0, the same
client version as CAP 10.0.1. The Runner references `Automaton.Messaging` for the
contract, but does not reference CAP or a database package. CAP is a test-only
dependency used to verify the actual serializer's output.

The probe reads existing `RabbitMq` configuration keys. Environment equivalents
use the standard .NET double underscore separator:

| Key | Value |
| --- | --- |
| `RabbitMq__Host` | Required broker host |
| `RabbitMq__Port` | Broker port, default `5672` |
| `RabbitMq__Username` | Required broker username |
| `RabbitMq__Password` | Required broker password |
| `RabbitMq__VirtualHost` | Default `/` |
| `RabbitMq__ExchangeName` | Default `cap.default.router`, matching current CAP configuration |

The exchange is durable and has type `topic`. Each host has one durable,
non-exclusive, non-auto-delete queue named `automaton.infrastructure.<RunnerHostId:N>`.
It binds to `infrastructure-command.requested`. No queue is created per Action,
application, or operation.

This simple binding broadcasts the topic to each host queue. The consumer checks
both durable identities before executing. Wrong-host copies are rejected at the
execution boundary and remain unacknowledged in this probe. This is not a final
multi-host routing or rejection policy. Do not enable production publication with
this Stage 2 probe.

Runner identity comes from `FileStorageService.GetRunnerId`, which already reads
`AUTOMATON_RUNNER_ID` or the enrolled `runner.json`. Host identity and purpose come
from `installation-profile.json`, with environment configuration used only when
there is no profile. An incomplete or non-Infrastructure profile fails closed.
Neither loading identity nor consuming a command contacts the API or SignalR.

`HandleAsync` accepts CAP's JSON payload and checks nonempty command/operation IDs,
matching Runner and host IDs, the shared trusted Action allowlist, input/grant
presence, and UTC creation time. Rejected commands never reach the Action executor.

## Trusted execution and packaging

`TrustedInfrastructureActions` in Core contains the existing seven-key allowlist.
Both the existing API preparation service and the new Runner adapter use it.

The Runner project copies system-owned manifests and Python entrypoints from
`system-apps/automaton-infrastructure/actions` into
`SystemApps/automaton-infrastructure/actions` beside the executable. These files
remain external content for single-file publish and are included in tool packages.
No source is fetched from the API or SignalR.

The Dockerfile now uses the repository root as its build context so it can copy
the same source directory. The Azure Runner Docker pipeline uses that context and
checks all seven manifests and Python entrypoints before pushing its image. Local
Docker builds are validation artifacts, not production releases.

Build from the repository root with
`docker build -f src/Automaton.Runner/Dockerfile -t automaton-runner .`.

`TrustedInfrastructureActionExecutor` calls the new
`AppActionDeploymentService.PrepareBundledInfrastructureAsync` adapter. That adapter
uses the existing manifest parser, content hashing, Python preparation, versioning,
and `PrepareAsync` implementation. Execution then calls the unchanged
`AppActionExecutionService.ExecuteAsync` and reads its existing result artifact.
No Docker operation or Python Action implementation is copied or rewritten.

The trusted platform app ID identifies the workspace. The durable host ID supplies
the Runner-local deployment target namespace, without looking up an API-owned
deployment target. `CommandId` is the existing executor's execution ID. The API's
current SignalR target directory remains unchanged during this stage.

Commands with secret grant IDs, and all provision/reprovision commands, stop before
Action preparation with an explicit unsupported-secret-delivery error. The adapter
does not resolve grants or inject secret values. Stage 3 owns that boundary.

## Probe lifetime and verification

The probe receives at most one delivery with prefetch `1` and `autoAck: false`.
It returns the existing execution result and result artifact in memory. It sends
neither ACK nor NACK. Closing its channel returns the delivery to RabbitMQ even
when execution succeeded. Invalid messages also remain queued. Run this only on
an isolated development broker with disposable test commands.

The existing Action executor still owns its local execution artifacts. Stage 2
adds no command-result store, callback, retries, scheduler, or crash recovery.
Existing executor fencing is not a claim of production-safe recovery.

Runner tests cover addressing rejection, the shared allowlist and local bundle,
identity resolution, CAP serialization, one executor invocation, and the existing
Python Action's container-name guard through real local preparation and execution.
The latter uses an invalid container name so no Docker mutation can occur.

The optional RabbitMQ test connects only to `127.0.0.1` with guest credentials when
`AUTOMATON_TEST_RABBITMQ_PORT` is set. It proves the consumer's topic binding and
checks broker redelivery after the probe returns without acknowledgement. It
removes only its test host's queue. Normal unit tests need no broker.

Stage 2 ends before HTTP callbacks, production ACK policy, live publication,
Hosted Runner lifecycle changes, or removal of SignalR execution.
