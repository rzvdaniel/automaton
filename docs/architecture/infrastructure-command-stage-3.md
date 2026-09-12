# Infrastructure command completion, Stage 3

Stage 3 completes the opt-in Infrastructure Runner command path. Normal Runner
startup still does not start the RabbitMQ consumer, and production Hosted Runner
operations still use SignalR.

The Runner uses its enrolled API key to request only the `SecretGrantIds` named in
the command from `POST /api/infrastructure-commands/{commandId}/secrets`. The API
loads the authenticated key ID from the authentication claim, then requires the
matching Infrastructure Runner, assigned Runner Host, operation, Action key, and
existing grant scope. `ExecutionSecretGrantService` remains the only secret
resolver. Secret values never travel in RabbitMQ and the Runner has no database
access.

The Runner posts its terminal execution and existing result artifact to
`POST /api/infrastructure-commands/{commandId}/result`. The API repeats the same
caller and command authorization. It stores a SHA-256 fingerprint on the command
execution. An identical terminal callback is accepted without reapplying lifecycle
changes. A different terminal callback is rejected. Successful results reuse
`InfrastructureActionProvisioningHandler.ApplySuccessfulOperationAsync`.

The consumer acknowledges a targeted delivery only after the callback returns
success. A callback failure leaves the delivery unacknowledged. The local Action
execution state fences redelivery: terminal states are read and their callback is
resent, while an interrupted state remains `Lost` with the existing ambiguous
execution behavior. Wrong-host broadcast copies are acknowledged without execution
so they do not block unrelated host queues.

This stage does not start the consumer in normal service mode, publish production
commands, remove SignalR, add polling, or change Generic Runtime behavior.
