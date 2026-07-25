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

Stage 2 persists and validates schedules only. It does not poll or execute them.
