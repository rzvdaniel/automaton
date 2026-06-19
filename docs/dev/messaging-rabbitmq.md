# RabbitMQ Messaging In Automaton

This document describes the current internal messaging shape used for Automaton registration events.

The goal is to keep Automaton free of framework lock-in while still allowing reliable event-driven side effects such as email, analytics, notifications, and audit enrichment.

## Latest updates

The current architecture was recently tightened in four important ways:

1. Producer-side CAP transaction code is now hidden behind `ITransactionalEventExecutor`.
2. Consumer-side business handling is now hidden behind `IEventHandler<TEvent>`.
3. Shared EF/data/email primitives were moved out of `Automaton.Api` into `Automaton.Infrastructure`.
4. Reusable identity primitives were extracted into `Automaton.Identity`.

That means:

- `AccountController` no longer depends on CAP-specific types
- `AccountRegistrationService` no longer depends on `ICapPublisher`
- `UserRegisteredCapSubscriber` is now only a transport adapter
- `UserRegisteredEventHandler` is now the transport-agnostic business handler
- `ApplicationDbContext` and shared entities no longer live only in `Automaton.Api`
- the Worker can use identity and email primitives without depending on the API assembly

This is the current intended boundary:

- application code depends on Automaton abstractions
- adapter code depends on CAP and RabbitMQ

## Architecture summary

Automaton currently uses:

- `Automaton.Api` as the producer
- `Automaton.Worker` as the consumer
- `Automaton.Infrastructure` for shared EF/data/email infrastructure
- `Automaton.Identity` for shared identity primitives
- CAP as the .NET messaging abstraction and reliability layer
- RabbitMQ as the transport broker
- MySQL as CAP's persistent message store

That means RabbitMQ is not the whole system by itself.

The complete flow is:

1. The API handles a user registration request.
2. The API saves the new user to MySQL.
3. In the same CAP-backed transaction, the API records an outgoing event message.
4. CAP publishes that event to RabbitMQ.
5. The Worker subscribes to the event through CAP.
6. CAP delivers the message to the Worker subscriber.
7. The Worker performs background side effects such as email, notifications, analytics, or audit enrichment.

The important design point is that the registration endpoint owns the main business action, while the Worker owns side effects that should happen after registration succeeds.

## Validated pattern

`UserRegisteredEvent` is now the validated reference implementation for future Automaton events.

What was verified locally:

- RabbitMQ runs in Docker locally
- `Automaton.Api` publishes successfully through CAP
- `Automaton.Worker` receives successfully through CAP
- CAP records published and received entries in MySQL
- registration confirmation and admin-notification emails are sent from the Worker path

This is the pattern to repeat for future events:

1. define the shared event contract in `Automaton.Messaging`
2. publish from API/application code through `IEventPublisher`
3. if the main business action should create durable audit history, write that audit event in the API/application flow when the main action succeeds
4. if publish must reflect a committed DB change, wrap it in `ITransactionalEventExecutor`
5. publish the integration event from the same application flow
6. add a thin CAP subscriber in `Automaton.Worker`
7. put business side effects in `IEventHandler<TEvent>`
8. keep Worker delivery details as operational logs unless there is a strong reason to add separate audit events
9. make sure the Worker host has the configuration required by those side effects
10. verify end to end before moving on to the next event

## What each part is responsible for

### CAP

CAP is the application-level event bus abstraction used inside the .NET code.

In Automaton, CAP is responsible for:

- publishing messages from application code
- subscribing messages to handler methods
- integrating message publishing with the database transaction
- tracking sent and received messages in MySQL
- handling retry and bookkeeping behavior

CAP is the reason the application code does not have to manage raw RabbitMQ channels, manual acknowledgements, or custom retry loops.

### RabbitMQ

RabbitMQ is the transport broker.

In Automaton, RabbitMQ is responsible for:

- carrying messages from the API process to the Worker process
- buffering messages when producer and consumer are not running at exactly the same time
- decoupling API request handling from background work

RabbitMQ is not where Automaton business logic lives. It is only the transport layer.

### MySQL

MySQL is used for two separate things in this architecture:

- Automaton application data such as users
- CAP message persistence for outgoing and incoming message tracking

This is why the Worker needs a MySQL connection string even though it is "just a consumer". CAP uses the database for reliability, not just RabbitMQ.

### Automaton.Infrastructure

`Automaton.Infrastructure` contains backend primitives that both the API and Worker need.

Its responsibilities are:

- host `ApplicationDbContext`
- host shared EF entities
- host shared config objects such as `AppConfig` and `EmailConfig`
- host email transport services such as `IEmailService`, `EmailService`, and `EmailConfirmationLinkBuilder`

This project should not depend on `Automaton.Api` or `Automaton.Worker`.

### Automaton.Identity

`Automaton.Identity` contains reusable identity-level behavior that is not HTTP-specific.

Its responsibilities are:

- load users by id
- generate email confirmation tokens
- orchestrate registration notification behavior that depends on identity primitives

This keeps the Worker from depending on API services like `UsersService` or controller helpers.

### Automaton.Api

`Automaton.Api` is the producer for registration events.

Its responsibilities are:

- validate and process the HTTP request
- create the user
- publish the domain event that registration happened
- return the HTTP response without waiting for background side effects

The API should not own long-running or failure-prone side effects once those have been moved to the Worker.

### Automaton.Worker

`Automaton.Worker` is the consumer for background event handling.

Its responsibilities are:

- subscribe to events
- run post-registration side effects
- retry when background work fails
- keep background processing separate from the API request path

That separation is the main reason to introduce messaging for registration in the first place.

## Design goals

- Keep `Automaton.Messaging` small.
- Keep event contracts shared between API and Worker.
- Keep the API unaware of RabbitMQ implementation details.
- Keep the Worker responsible for background event handling.
- Make it possible to replace RabbitMQ later without rewriting controller code.

## Project layout

Current messaging-related pieces:

```text
src/
  Automaton.Messaging/
    Abstractions/
      IEventPublisher.cs
      IEventHandler.cs
      ITransactionalEventExecutor.cs
    Events/
      UserRegisteredEvent.cs
    EventTopics.cs

  Automaton.Infrastructure/
    Data/
      ApplicationDbContext.cs
    Email/
      EmailConfirmationLinkBuilder.cs
      EmailService.cs
      IEmailService.cs

  Automaton.Identity/
    Abstractions/
      IEmailConfirmationTokenService.cs
      IUserLookupService.cs
    Services/
      EmailConfirmationTokenService.cs
      UserLookupService.cs
      UserRegistrationNotificationService.cs

  Automaton.Api/
    Services/
      AccountRegistrationService.cs
    Messaging/
      CapEventPublisher.cs
      CapTransactionalEventExecutor.cs

  Automaton.Worker/
    Handlers/
      UserRegisteredEventHandler.cs
    Consumers/
      UserRegisteredCapSubscriber.cs
```

## Shared contract layer

`Automaton.Messaging` contains:

- event contracts
- small abstractions
- shared topic constants

It should not contain broker plumbing or host-specific CAP registration.

Current abstraction:

```csharp
public interface IEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class;
}
```

This keeps the API dependent on an application-level contract rather than broker-specific types.

Current shared abstractions also include:

- `ITransactionalEventExecutor`
- `IEventHandler<TEvent>`

These exist so CAP-specific transaction code and CAP-specific subscriber code stay in adapter classes instead of spreading into controllers or business handlers.

## Why the API and Worker are split

Registration should succeed if the core user creation succeeds.

Welcome email, admin notification, analytics, and audit enrichment are side effects. Those are valuable, but they should not be allowed to make the registration request fail or become slow.

The split therefore looks like this:

- API: synchronous, user-facing, request/response path
- Worker: asynchronous, internal, side-effect path

That gives Automaton:

- faster registration responses
- fewer user-facing failures caused by email or notification issues
- cleaner separation between core actions and follow-up work
- a foundation for more background events later

## Current event

The first event is:

```text
UserRegisteredEvent
```

Location:

- [src/Automaton.Messaging/Events/UserRegisteredEvent.cs](/C:/Work/automaton/src/Automaton.Messaging/Events/UserRegisteredEvent.cs)

It represents a fact that already happened, so it uses past-tense naming.

`UserRegisteredEvent` also includes an application-level `CorrelationId` string.

That value is separate from CAP's own internal correlation headers.
In the current implementation it is populated from the current request trace, preferring `Activity.Current.TraceId` and falling back to `HttpContext.TraceIdentifier`.

## Project boundary rule

The intended dependency direction is now:

```text
Automaton.Api
  -> Automaton.Messaging
  -> Automaton.Identity
  -> Automaton.Infrastructure

Automaton.Worker
  -> Automaton.Messaging
  -> Automaton.Identity
  -> Automaton.Infrastructure

Automaton.Identity
  -> Automaton.Infrastructure
```

The important rule is:

- API and Worker may depend on shared backend libraries
- shared backend libraries must not depend on API or Worker

This is why `UserRegistrationNotificationService` belongs in `Automaton.Identity`, not `Automaton.Infrastructure`: it needs identity primitives such as user lookup and token generation.

## API publishing flow

The API publishes registration events through `IEventPublisher`.

Current implementation:

- `AccountController` validates the request and builds the user object
- `AccountRegistrationService` calls `ITransactionalEventExecutor`
- `AccountRegistrationService` saves the user
- `AccountRegistrationService` publishes `UserRegisteredEvent`

Locations:

- [src/Automaton.Api/Controllers/AccountController.cs](/C:/Work/automaton/src/Automaton.Api/Controllers/AccountController.cs)
- [src/Automaton.Api/Services/AccountRegistrationService.cs](/C:/Work/automaton/src/Automaton.Api/Services/AccountRegistrationService.cs)

The controller should not know RabbitMQ channel details or CAP transaction details.

The practical meaning is:

- controller code says "register this user"
- application/infrastructure code decides how that registration is persisted and published

That keeps controller code stable even if the transport changes later.

## CAP publisher implementation

CAP publishing is currently implemented in:

- [src/Automaton.Api/Messaging/CapEventPublisher.cs](/C:/Work/automaton/src/Automaton.Api/Messaging/CapEventPublisher.cs)
- [src/Automaton.Api/Messaging/CapTransactionalEventExecutor.cs](/C:/Work/automaton/src/Automaton.Api/Messaging/CapTransactionalEventExecutor.cs)

Those classes:

- publish to shared event topic constants
- delegate the actual publish call to `ICapPublisher`
- wrap the CAP-backed EF transaction boundary behind `ITransactionalEventExecutor`

API DI registration lives in:

- [src/Automaton.Api/Program.cs](/C:/Work/automaton/src/Automaton.Api/Program.cs)

`Program.cs` configures CAP with:

- `UseEntityFramework<ApplicationDbContext>()`
- `UseRabbitMQ(...)`

`UseEntityFramework<ApplicationDbContext>()` is important because it lets CAP participate in the same EF/MySQL transaction used by registration.

That CAP-specific transaction boundary currently lives in:

- [src/Automaton.Api/Messaging/CapTransactionalEventExecutor.cs](/C:/Work/automaton/src/Automaton.Api/Messaging/CapTransactionalEventExecutor.cs)

This is an intentional boundary choice:

- `ICapPublisher` is allowed in CAP adapter code
- `ICapPublisher` is not allowed in the controller
- `ICapPublisher` is not allowed in `AccountRegistrationService`

That is the reliability story:

- if the user save fails, the event is not committed
- if the transaction is committed, CAP has a persisted outgoing message to publish

This is much better than "save the user, then separately try to push to RabbitMQ with no transaction coordination".

## Worker consumer flow

The Worker consumes registration events using a CAP subscriber:

- [src/Automaton.Worker/Consumers/UserRegisteredCapSubscriber.cs](/C:/Work/automaton/src/Automaton.Worker/Consumers/UserRegisteredCapSubscriber.cs)
- [src/Automaton.Worker/Handlers/UserRegisteredEventHandler.cs](/C:/Work/automaton/src/Automaton.Worker/Handlers/UserRegisteredEventHandler.cs)

The current implementation:

- subscribes to the `user.registered` topic
- forwards the message to `IEventHandler<UserRegisteredEvent>`
- lets CAP handle retries and bookkeeping

Worker host registration lives in:

- [src/Automaton.Worker/Program.cs](/C:/Work/automaton/src/Automaton.Worker/Program.cs)

The Worker configures CAP with:

- `UseMySql(...)`
- `UseRabbitMQ(...)`

The Worker subscriber does not talk to RabbitMQ directly. CAP receives the message, resolves the subscriber, and invokes the handler method.

That means the Worker code stays focused on business side effects instead of messaging plumbing.

`UserRegisteredEventHandler` is the portable business handler.

`UserRegisteredCapSubscriber` is the CAP-specific adapter.

`UserRegisteredEventHandler` is no longer log-only.

It now calls `UserRegistrationNotificationService`, which sends:

- the user confirmation email
- the admin new-registration notification

Failures are logged and rethrown so CAP retry behavior remains active.

## Current topology

Current shared topic constants are defined in:

- [src/Automaton.Messaging/EventTopics.cs](/C:/Work/automaton/src/Automaton.Messaging/EventTopics.cs)

Current values:

- `EventTopics.UserRegistered = "user.registered"`

This is intentionally simple for the first event flow.

## Message lifecycle in Automaton

For `UserRegisteredEvent`, the message lifecycle is:

1. `AccountController` validates the request and builds the registration input.
2. `AccountRegistrationService` asks `ITransactionalEventExecutor` to run the workflow transactionally.
3. `CapTransactionalEventExecutor` opens the CAP-backed EF transaction.
4. `CapEventPublisher` publishes the event to the CAP topic `user.registered`.
5. CAP records the outgoing message in its persistence tables.
6. CAP sends the message through RabbitMQ.
7. CAP in the Worker receives the message from RabbitMQ.
8. CAP invokes `UserRegisteredCapSubscriber.HandleAsync(...)`.
9. `UserRegisteredCapSubscriber` forwards the message to `UserRegisteredEventHandler`.
10. `UserRegisteredEventHandler` runs the business handling logic.

This is the key mental model:

- API code publishes an event
- CAP persists and transports it
- Worker adapter receives it
- Worker handler processes it

## Why CAP + RabbitMQ instead of raw RabbitMQ

Raw RabbitMQ would require Automaton to own more infrastructure code such as:

- connection lifecycle
- channel lifecycle
- serialization conventions
- publish confirmations
- retry behavior
- acknowledgement behavior
- error handling behavior

CAP removes most of that plumbing while still letting RabbitMQ remain the transport underneath.

So the architecture is not "replace RabbitMQ".
It is "use CAP on top of RabbitMQ so application code stays simpler and more reliable".

## Local development

RabbitMQ can be started with:

```bash
docker compose -f docker-compose.rabbitmq.yml up -d
```

Management UI:

```text
http://localhost:15672
```

Typical local config:

```json
"RabbitMq": {
  "Host": "localhost",
  "VirtualHost": "/",
  "Username": "automaton",
  "Password": "automaton"
}
```

The Worker also needs a MySQL connection string because CAP stores published/received message state in the database.

Related files:

- [docker-compose.rabbitmq.yml](/C:/Work/automaton/docker-compose.rabbitmq.yml)
- [docker-compose.local.yml](/C:/Work/automaton/docker-compose.local.yml)
- [.env.example](/C:/Work/automaton/.env.example)

## Registration side effects today

The registration flow is now fully moved to the Worker side for post-registration side effects:

- `AccountRegistrationService` creates the user and publishes `UserRegisteredEvent`
- `AccountController` no longer owns the registration confirmation/admin-notification side effects
- `UserRegisteredEventHandler` calls `UserRegistrationNotificationService`
- `UserRegistrationNotificationService` generates the email confirmation token, builds the confirmation link, sends the user email, and sends the admin notification

This is now the reference example for future background side-effect migrations.

## Migration rule

When migrating side effects from synchronous API code into the Worker:

- verify publish/consume works end to end first
- move one side effect at a time
- never leave the same side effect active in both API and Worker

The safe order for future events is:

1. API publishes event
2. Worker logs event only
3. verify pipeline
4. move the side effect into the Worker handler
5. remove the direct API side effect from the API host
6. verify that no duplicate side effects are produced

This avoids duplicate emails and duplicate admin notifications.

One important implementation detail:

- if the Worker side effect needs host configuration such as `EmailConfig`, `AppConfig`, or identity/token services, wire that into `Automaton.Worker` before enabling the side effect
- do not assume config previously used only by the API host is automatically available in the Worker host
- for account lifecycle flows such as registration, keep the main audit event in the API flow and keep Worker success/failure details as operational logs unless audit requirements clearly justify more

## Why CAP here

CAP keeps the application-level abstraction but removes the need to manage raw RabbitMQ connections, channels, acknowledgements, and retries ourselves.

It also adds the outbox/inbox storage model on top of MySQL, which is the main reason the API publish path now uses a CAP-backed EF transaction.

For Automaton, that is a good fit because:

- the API already uses EF Core and MySQL
- the Worker is already a separate process
- registration side effects are a natural asynchronous use case
- the codebase benefits from less transport plumbing

## Future replacement options

Because the API depends on `IEventPublisher`, CAP can still be replaced later if needed with:

- Azure Service Bus
- Kafka
- Rebus
- Wolverine

The important rule is:

- do not let controllers depend directly on transport/framework-specific types

## Portable vs non-portable pieces

Portable pieces in the current shape:

- `UserRegisteredEvent`
- `IEventPublisher`
- `ITransactionalEventExecutor`
- `IEventHandler<UserRegisteredEvent>`
- `UserRegisteredEventHandler`
- `AccountRegistrationService`
- `IUserLookupService`
- `IEmailConfirmationTokenService`
- `UserRegistrationNotificationService`

Transport/framework-specific pieces in the current shape:

- `CapEventPublisher`
- `CapTransactionalEventExecutor`
- `UserRegisteredCapSubscriber`
- CAP subscriber attributes
- RabbitMQ transport configuration

That is the intended containment model.

## Verification checklist

For local end-to-end verification:

1. Start RabbitMQ.
2. Start `Automaton.Worker`.
3. Start `Automaton.Api`.
4. Register a user.
5. Confirm CAP writes rows to `cap.published` and `cap.received`.
6. Confirm the Worker handles the event and performs the intended side effect.
7. Confirm the queue drains back to zero.
8. Confirm no error queue or retry storm appears.
9. Confirm no duplicate side effects are produced.
