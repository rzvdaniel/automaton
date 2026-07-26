# Action schedule syntax

Action schedules support one-time UTC timestamps, fixed intervals, and standard five-field cron expressions:

```text
minute hour day-of-month month day-of-week
```

Cron values are interpreted in the schedule's IANA time zone. Automaton uses Cronos semantics: both day fields are combined with AND when both are restricted, occurrences skipped by daylight-saving transitions are not invented, and duplicated local times produce one occurrence.

Cron occurrences must respect the greater of the subscription plan and Action manifest minimum intervals. Validation examines consecutive occurrences across a bounded annual horizon (up to 100,000 occurrences).

Misfire behavior is limited to `Skip` and `RunOnceNow`. `CatchUp` is reserved but rejected until dispatch implements an explicit bound.

Schedules have explicit ownership:

- `User` schedules belong to a user, use only `AppRunner`, and remain subject to that user's current plan and app permissions.
- `System` schedules have no human owner, target the trusted platform application, and can be managed only by administrators.

One-time API timestamps must include `Z` or an explicit UTC offset. Only the field belonging to the selected schedule type is persisted.

The Worker polls due schedules every 20 seconds. A conditional database update
claims each schedule with a two-minute lease, so competing Worker instances cannot
create the same occurrence. The occurrence and its CAP outbox message are committed
in one transaction. Each occurrence has the stable key
`schedule:<schedule-id>:<occurrence-utc>`; the API also enforces a unique
occurrence-to-execution link for redelivery safety.

The Worker owns schedule calculation, misfire handling, concurrency policy, and
trusted in-process Worker handlers. The API owns App Runner and Infrastructure
Runner dispatch. Infrastructure targets are resolved from server-side host and
Runner records; the administrator-provided host ID never selects an arbitrary
Runner connection directly. Routing metadata should move out of Action input
before generic System schedule editing is exposed.
Dispatch revalidates the current plan, authorization, Action manifest, scope, and
input before execution.

Before a trusted Worker handler runs, the Worker persists a `Running` execution
and links it to the occurrence. Handlers receive the execution ID, occurrence ID,
and occurrence idempotency key. If the Worker stops after acceptance without
recording a terminal result, stale recovery marks the execution `Lost` and does
not replay potentially completed side effects automatically.

`Allow`, `SkipIfRunning`, and `Queue` are supported. `Skip` and `RunOnceNow`
misfires advance the schedule without waiting for execution completion, while
`CatchUp` remains rejected. Expired claims and dispatch leases are recoverable;
disabling a schedule cancels occurrences that have not started dispatch.
An automatically completed one-time schedule does not cancel its durable queued
occurrence; it remains eligible after concurrency capacity becomes available.
