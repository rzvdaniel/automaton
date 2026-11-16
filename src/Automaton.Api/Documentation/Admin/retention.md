---
title: Retention of Action runs, logs and audit events
order: 30
---

# Retention of Action runs, logs and audit events

A nightly Worker job (04:00 UTC) deletes what is past the limits, in small batches, and writes how much it removed to the Worker log. Nothing is kept forever any more.

| Item | Default | Rule |
| --- | --- | --- |
| Action run records | 30 days | Never fewer than the last 20 runs per Action. Failed runs are kept twice as long. |
| Action run logs | 7 days | Failed runs' logs are kept twice as long. Expired logs are no longer offered by the API. |
| `home-card` runs | last 3 | Plus failed runs, kept as for other Actions. |
| Audit events (Admin, Logs) | 365 days | Secret vault events keep their own 180 day rule. |

The last run and next run of an app stay correct: the newest runs per Action are never deleted.

## Change the limits

Administrators use the API (a Studio page is not built yet):

- `GET` and `PUT /api/admin/retention` read and set the server defaults (`runDays`, `minimumRunsPerAction`, `logDays`, `failedMultiplier`, `auditDays`, `homeCardRunsToKeep`).
- `GET`, `PUT` and `DELETE /api/admin/retention/apps/{appId}` read, set and clear a per-app override of `runDays`, `minimumRunsPerAction` and `logDays`. The override wins over the default.

Keeping runs or logs more than 90 days needs the extra `?approveLongRetention=true`; without it the request is refused with 400.
