# Action schedule syntax

Action schedules support one-time UTC timestamps, fixed intervals, and standard five-field cron expressions:

```text
minute hour day-of-month month day-of-week
```

Cron values are interpreted in the schedule's IANA time zone. Automaton uses Cronos semantics: both day fields are combined with AND when both are restricted, occurrences skipped by daylight-saving transitions are not invented, and duplicated local times produce one occurrence.

Stage 2 persists and validates schedules only. It does not poll or execute them.
