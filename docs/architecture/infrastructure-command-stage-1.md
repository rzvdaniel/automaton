# Infrastructure command publication, Stage 1

Stage 1 added a command contract and CAP publication support in the API and Worker.
The legacy SignalR execution path described by the original implementation has
since been removed; Hosted Runner infrastructure operations now use the durable
command path exclusively. Generic Action execution and scheduled Actions retain
their existing SignalR support.

## Contract and addressing

`Automaton.Messaging.Events.InfrastructureCommandRequestedEvent` contains:

| Field | Source |
| --- | --- |
| `CommandId` | Existing `OperationId`, reused as the stable command identity |
| `OperationId` | Existing Hosted Runner operation ID |
| `RunnerHostId` | Existing request's host ID |
| `InfrastructureRunnerId` | Explicit target Runner ID supplied by the caller |
| `ActionKey` | Existing trusted Action key |
| `InputJson` | Existing input, unchanged |
| `SecretGrantIds` | Copy of the existing grant IDs, never resolved secret values |
| `CreatedAtUtc` | UTC publication time supplied by the caller |

There is one command per Hosted Runner operation. Conversion reuses that
operation's ID rather than generating a new command ID on every attempt. The
separate fields express command and lifecycle identities without a new ID store.
A later live caller must supply the assigned `RunnerHost.InfrastructureRunnerId`
and preserve the original command on retries. Neither addressing field comes
from SignalR presence or API-instance ownership.

The command omits the old event's server-only `UserId`, `HostedRunnerId`, and
`CorrelationId` fields. The API can resolve operation ownership from `OperationId`.
Hosted Runner identity needed by an Action remains in its existing `InputJson`.

## Publication seam

The original lifecycle request conversion populated this contract. Existing
`IEventPublisher.PublishAsync` then dispatches it through either the API or
Worker `CapEventPublisher` to `infrastructure-command.requested`. All lifecycle
Actions share that topic.

The new type separates the Runner payload from the old API request. Reusing the
old type for both topics would make the existing publisher's type-based dispatch
ambiguous and keep server-only fields in the Runner contract. This is a temporary
conversion at the old request boundary, not a second publisher abstraction.

Publication uses the existing CAP storage and RabbitMQ configuration. Future live
callers must use the existing transactional event executor when saving lifecycle
state and publishing together. Stage 1 adds no exchange topology, queue, routing
headers, consumer, or broker credentials. Addressing is explicit in the payload;
physical queue routing remains future work. Publication to an unbound topic is
not proof that RabbitMQ will retain a command for a future consumer.

The tests are the only callers of the conversion and new publication path. This
avoids a backlog of commands for operations already executed by SignalR. There is
no runtime switch to enable parallel publication accidentally.

## Deletion after cutover

The legacy Hosted Runner SignalR event, subscriber, connection-affine lifecycle
dispatch, and delivery-recovery code were removed after cutover. Lifecycle result
application remains behind the durable callback boundary. The trusted Action
deployment service remains because scheduled Infrastructure Runner Actions still
use the generic SignalR Action API.

## Verification boundary

Focused tests cover conversion for all seven Action keys, serialized fields,
stable operation identity, host and Runner addressing, unchanged input, grant-ID
copying, and CAP topic selection in both server processes. The exact serialized
field assertion excludes connection IDs, API instance/color data, heartbeat data,
Docker connection details, and secret-value fields. Existing event publication
tests assert that the old request still produces only its old topic.

These tests use a recording CAP publisher. They do not claim broker delivery,
consumption, or crash recovery. Action packaging, secret retrieval, HTTP results,
Runner consumption, production operations, and later migration stages remain out
of scope. No Admin documentation change is needed because Stage 1 adds no
administrator-visible behavior or configuration.
