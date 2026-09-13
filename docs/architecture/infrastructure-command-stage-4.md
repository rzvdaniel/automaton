# Infrastructure command cutover, Stage 4

Provision and reprovision now publish exactly one `InfrastructureCommandRequestedEvent`
with `CommandId == OperationId`, the persisted `RunnerHostId`, its assigned
`InfrastructureRunnerId`, and the trusted `provision-hosted-runner` or
`reprovision-hosted-runner` Action key. They no longer publish
`InfrastructureActionExecutionRequestedEvent`; that subscriber remains for the
unmigrated lifecycle actions.

An enrolled Infrastructure Runner starts the durable consumer as part of normal
host lifetime. The canonical Infrastructure Runner cloud-init/install payload
writes the privileged Runner env file with `RabbitMq__Host`, `RabbitMq__Port`,
`RabbitMq__Username`, `RabbitMq__Password`, `RabbitMq__VirtualHost`, and
`RabbitMq__ExchangeName` alongside the enrollment settings. The file is mode
0600 and shredded after the container is started; these values are not passed to
Managed Hosted Runner containers or placed in command messages. Port defaults to
5672, virtual host to `/`, and exchange to `cap.default.router` when omitted.
The consumer logs the missing configuration names and stays disabled if the
privileged Runner is not fully configured. It uses prefetch 1, manual
acknowledgement, and RabbitMQ.Client topology recovery. Managed and standard
Runners do not start it. Repair/reinstall regenerates the same env payload from
the persisted provisioning configuration, preserving the identity/configuration
reconnect invariant.

The API and Worker continue to use the Docker-internal `RabbitMq:Host=rabbitmq`
coordinate. Remote Infrastructure Runners use the separate, explicitly
configured `InfrastructureRunnerRabbitMq:Host` and `Port` values, which must
refer to a trusted private broker endpoint. Production Compose publishes AMQP
only on that private host address; it does not publish AMQP or management on
the public interface. The endpoint resolver fails closed when the runner-facing
host is not configured and reuses the canonical RabbitMQ credentials, virtual
host, and exchange settings.

The lifecycle reconciler does not republish provision or reprovision merely
because the API-side execution record is not present; RabbitMQ is the durable
handoff. Other lifecycle actions retain their existing recovery and SignalR path.

Stage 4 is implementation-only. It does not deploy production, migrate other
actions, or remove broad SignalR infrastructure.
