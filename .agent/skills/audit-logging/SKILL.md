---
name: audit-logging
description: Use when adding, reviewing, or refactoring audit events, audit DTOs/queries, audit metadata, or diagnostic logging decisions in Studio backend or Runner services.
license: Apache-2.0
compatibility: Repo-local agent skills for coding agents that can read .agent/skills.
metadata:
  author: Automaton
  version: 1.0.0
---

# Audit Logging

Use this skill when touching:

- audit event entities, DTOs, queries, services, or controllers
- audit event constants and naming
- metadata sanitization or redaction
- backend diagnostic logging levels
- auth/API key/token logging
- infrastructure service logging
- admin audit history APIs and UI-backed query behavior

Primary areas:

- `src/Automaton.Api/**`
- `src/Automaton.Runner/**`
- `src/UnitTests/**` for audit/logging tests

## Core split

- Infrastructure services do not decide audit history.
- Business/application services decide audit history.
- Infrastructure services should almost never write audit events directly.

Examples of infrastructure services:

- `EmailService`
- file/blob storage services
- Git wrappers
- OpenAI/AI provider wrappers
- HTTP client wrappers
- runner client wrappers
- SMTP providers
- queue clients

Examples of business/application services:

- `UsersService`
- `AppsService`
- `RunnerService`
- hosted-runner orchestration services
- admin/domain services that implement user-visible actions

Preferred layering:

```txt
UsersService.InviteUser()
 ├─ Audit: UserInvited
 └─ EmailService.Send(...)
      └─ Debug: Email sent
```

## What belongs in audit

Audit events should capture meaningful business history:

- security/account actions
- admin changes
- API key lifecycle
- app lifecycle
- runner lifecycle
- hosted runner operations when user-visible or operationally important
- organization membership/role/invitation events

Audit should answer:

- what changed
- which entity changed
- who initiated it
- which user was targeted, if different
- whether it succeeded or failed

Audit should not capture implementation details such as:

- `EmailSent`
- `FileSaved`
- `BlobUploaded`
- `HttpRequestCompleted`
- `GitCloneSucceeded`

Those belong in diagnostics, not `AuditEvents`.

## Audit shape

- `Action` = generic verb
- `EventType` = specific business event
- `EntityType` = primary affected entity

Example:

```txt
EntityType = Runner
Action = Assigned
EventType = RunnerAssignedToApp
```

- Prefer `static class` + `const string` for actions, entity types, event types, and outcomes.
- Prefer explicit event types for async or failure-prone operations:
  - `Requested`
  - `Succeeded`
  - `Failed`

## Audit write pattern

- Prefer request-model based audit writes over long optional-parameter overloads.
- Standard shape:
  - action
  - entity type
  - entity id
  - event type
  - message
  - metadata
  - outcome
  - actor override when needed
  - target user id when relevant

## Metadata rules

- Metadata must be useful for operators and admins.
- Metadata must never contain secrets.
- Redact sensitive fields globally before persistence.

Never allow these into persisted audit metadata:

- `apiKey`
- `rawKey`
- `key`
- `secret`
- `token`
- `password`
- `hash`
- `connectionString`
- `authorization`

- Prefer storing display-safe identifiers such as names, ids, prefixes, modes, and statuses.
- If a value might expose credentials or connection material, redact it.

## Diagnostic logging rules

Infrastructure services should usually emit:

- `Debug` for successful steady-state operations
- `Warning` for recoverable failures, degraded behavior, retries, invalid attempts, or expected-but-important anomalies
- `Error` for unexpected failures or critical path breaks

Keep `Information` for:

- lifecycle transitions
- admin mutations
- billing/subscription lifecycle
- hosted-runner lifecycle
- publish/unpublish lifecycle
- runner connect/reconnect/activation lifecycle

Downgrade high-frequency request-path success logs to `Debug`.

## Secrets and auth logging

- Never log raw API keys, access tokens, refresh tokens, provider keys, passwords, connection strings, or `.env` values.
- Avoid logging even key prefixes unless there is a strong operator need.
- Prefer structured exception logging over interpolating exception messages into auth/token logs.
- Auth validation failures should usually be `Warning`; unexpected auth pipeline failures should usually be `Error`.

## Common mistakes

- Adding audit writes inside infrastructure helpers.
- Using generic `Updated` events where a specific event type exists.
- Logging successful request-path operations at `Information`.
- Including secret-adjacent metadata in audit JSON.
- Treating delivery details as audit history.

## Verification

When changing audit or logging behavior, run the smallest relevant checks:

```bash
dotnet test
```

Focused areas to cover when practical:

- audit write behavior
- target user handling
- filter/paging behavior for audit endpoints
- metadata redaction
- auth/API key logging paths for secret leakage

Useful grep checks:

```bash
rg -n -i "apiKey|rawKey|\\bkey\\b|secret|token|password|hash|connectionString|authorization" src/Automaton.Api src/Automaton.Runner
```
