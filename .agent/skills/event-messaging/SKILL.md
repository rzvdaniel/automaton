---
name: event-messaging
description: Use when adding, changing, or reviewing Automaton event publishing, CAP/RabbitMQ adapters, worker subscribers, or shared messaging contracts.
license: Apache-2.0
compatibility: Repo-local agent skill for coding agents that can read .agent/skills.
metadata:
  author: Automaton
  version: 1.0.0
---

# Event Messaging

Use this skill when touching:

- `src/Automaton.Messaging/**`
- `src/Automaton.Api/Messaging/**`
- `src/Automaton.Api/Services/*Registration*.cs`
- `src/Automaton.Worker/Consumers/**`
- `src/Automaton.Worker/Handlers/**`
- RabbitMQ or CAP wiring related to application events
- documentation for event architecture under `docs/dev/`

## Purpose

Automaton uses a contained event-driven architecture:

- application code depends on Automaton abstractions
- adapter code depends on CAP and RabbitMQ

The goal is not to eliminate all transport-specific code.
The goal is to contain transport-specific code so future transport changes are realistic.

## Current architecture shape

Shared backend split:

- `Automaton.Infrastructure` holds shared EF/data/email primitives
- `Automaton.Identity` holds reusable identity primitives
- `Automaton.Api` and `Automaton.Worker` may depend on both
- `Automaton.Identity` may depend on `Automaton.Infrastructure`
- `Automaton.Infrastructure` must not depend on `Automaton.Identity`, `Automaton.Api`, or `Automaton.Worker`

Producer side:

- `AccountController`
- `AccountRegistrationService`
- `IEventPublisher`
- `ITransactionalEventExecutor`
- CAP adapters underneath

Consumer side:

- `IEventHandler<TEvent>`
- event-specific handler such as `UserRegisteredEventHandler`
- transport adapter such as `UserRegisteredCapSubscriber`
- optional identity-backed side-effect orchestration such as `UserRegistrationNotificationService`

Shared layer:

- event contracts in `Automaton.Messaging/Events/`
- abstractions in `Automaton.Messaging/Abstractions/`
- topic constants in `Automaton.Messaging/EventTopics.cs`

## Hard rules

- Put event contracts in `src/Automaton.Messaging/Events/`.
- Put messaging abstractions in `src/Automaton.Messaging/Abstractions/`.
- Keep `src/Automaton.Messaging/` free of CAP, RabbitMQ, or host registration code.
- Keep shared dependency direction one-way: `Automaton.Identity -> Automaton.Infrastructure`.
- Controllers must not depend on `ICapPublisher`.
- Controllers must not depend on CAP subscriber concepts or RabbitMQ client concepts.
- Application services should prefer `IEventPublisher` and `ITransactionalEventExecutor`.
- CAP-backed transaction code must stay behind `ITransactionalEventExecutor`.
- CAP subscribers must be thin adapters.
- Business handling logic must live in `IEventHandler<TEvent>` implementations, not in CAP subscriber classes.
- Do not place identity-orchestrating services in `Automaton.Infrastructure` if they depend on identity abstractions.
- Shared topic names must live in `EventTopics`.
- Use past-tense event names for facts that already happened, such as `UserRegisteredEvent`.

## Allowed dependency boundaries

Allowed in adapter code:

- `ICapPublisher`
- CAP subscriber attributes
- CAP host registration
- RabbitMQ transport configuration

Not allowed in boundary/business code:

- `ICapPublisher` in controllers
- `ICapPublisher` in ordinary business/application services when an Automaton abstraction already exists
- RabbitMQ client concepts in controllers or handlers
- business side-effect logic directly inside CAP subscriber classes

## Producer workflow pattern

When adding a new event on the API side:

1. Add the event contract in `Automaton.Messaging`.
2. Add or update the topic constant in `EventTopics`.
3. Publish through `IEventPublisher`.
4. If the publish must be coordinated with a database save, execute it through `ITransactionalEventExecutor`.
5. Keep CAP-specific transaction code inside adapter code only.

## Consumer workflow pattern

When adding a new event on the Worker side:

1. Add a transport-agnostic handler implementing `IEventHandler<TEvent>`.
2. Add a thin CAP subscriber that receives the event and forwards it to the handler.
3. Keep retries and delivery concerns in CAP.
4. Keep business side effects in the handler.

## Migration rule for moving side effects

When moving side effects from API code into the Worker:

1. Publish the event first.
2. Add a Worker handler that only logs.
3. Verify end-to-end delivery.
4. Remove the direct API side effect.
5. Add the side effect inside the Worker handler.

If the moved side effect depends on host configuration such as `EmailConfig`, `AppConfig`, or auth/token settings, copy or bind the required configuration in `Automaton.Worker` before enabling the side effect. Do not assume API-only settings are available in the Worker host.

Never leave the same side effect active in both API and Worker unless duplicate delivery is explicitly intended.

## Common pitfalls

- Leaking `ICapPublisher` into controllers.
- Putting business logic directly inside `[CapSubscribe]` classes.
- Reintroducing RabbitMQ topology/plumbing into shared contracts.
- Scattering event topic strings instead of using `EventTopics`.
- Publishing outside the transactional boundary when the event must reflect a committed DB change.
- Moving email/admin notifications to the Worker before confirming the log-only pipeline works.
- Moving a side effect to the Worker without also wiring the required host configuration, especially `EmailConfig` for SMTP-backed email delivery.

## Verification

Run the smallest relevant verification for the files changed.

Typical checks:

```bash
dotnet build src/Automaton.Worker/Automaton.Worker.csproj
dotnet build src/Automaton.Api/Automaton.Api.csproj
```

For local end-to-end flow:

1. Start RabbitMQ.
2. Start `Automaton.Worker`.
3. Start `Automaton.Api`.
4. Trigger the event, currently user registration.
5. Confirm the Worker receives and logs the event.
6. Confirm no duplicate side effects occur during migration.

If normal build outputs are locked by running processes, use a temporary output folder for verification.

## Related docs and skills

- `docs/dev/messaging-rabbitmq.md`
- `.agent/skills/studio-backend/SKILL.md`
- `.agent/harness/verification.md`
