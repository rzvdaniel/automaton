# Web Apps - Technical Architecture & Implementation Plan

Status: Draft

## Overview

This document describes the technical architecture, design decisions and implementation plan for the Web Apps feature in Automaton Studio. It covers how published web apps load, how they call server flows, runner selection policy, security considerations, API design, UI changes and a practical implementation roadmap.

The goal is to provide a predictable, secure and easy-to-manage way for published web apps to execute flows on behalf of their owner while keeping the UX lightweight for app owners.

## Goals

- Enable users to create, preview and publish HTML/CSS/JS apps inside the Studio.
- Allow published apps to invoke flows via a secure server endpoint while executing flows as the app owner (impersonation).
- Provide a simple, low-friction way to control which runner executes web-app-initiated flow runs (preferred-per-app + user default + deterministic fallback).
- Maintain full traceability and auditing for every execution.
- Keep UI changes minimal and optional for users.

## High-level Architecture

- `Automaton.Studio.Web` — Blazor UI (editor, preview, publish flows)
 - `AppDesignerPage` — edit & preview HTML
 - `WebAppViewer.razor` — renders published app in a sandboxed iframe

- `Automaton.Studio.Server` — Backend APIs
 - `AppsController` — CRUD, publish/unpublish, `GET api/apps/slug/{slug}` for viewer
 - `WebAppFlowExecutionController` — `POST api/web-apps/{slug}/flows/{identifier}/execute`
 - `AppsService` — app business logic and DB access
 - `FlowsService` — flow business logic (resolves flows, executes)
 - `RunnerService` — runner inventory & execute orchestration
 - `UserImpersonationServiceFactory<T>` — create service instances impersonating an owner

- Runners (agents) — execute flows; registered with `RunnerService` and have `RunnerDetails` (Id, Name, Status, tags)

## Key Flows

### Loading a published web app
1. Browser navigates to `/web/{slug}`.
2. `WebAppViewer.razor` calls `AppsService.LoadBySlug(slug)` (or `GET api/apps/slug/{slug}`).
3. Backend validates published status and `RequiresAuthentication` and returns `AppModel` (or redirects to login if needed).
4. `WebAppViewer` renders `srcdoc` into a sandboxed `<iframe>`.

### Web app invoking a Flow
1. App JavaScript calls `POST /api/web-apps/{slug}/flows/{flowIdentifier}/execute` with optional `inputs` and `RunnerNames`.
2. `WebAppFlowExecutionController`:
 - Validates published status and `RequiresAuthentication`.
 - Determines `appOwnerId` (owner user id), logs access.
 - Creates impersonated `FlowsService` for `appOwnerId` via `UserImpersonationServiceFactory<FlowsService>`.
 - Resolves `flowIdentifier` -> `flowId` (GUID or by name via `FlowsService.ResolveFlowNameToId`).
 - Loads the flow using the impersonated service and applies input variables.
 - Selects runners using `RunnerSelector` policy (see Runner Selection below).
 - Executes using `FlowsService.Execute(flowId, runnerIds, cancellationToken)` which typically delegates to `RunnerService.ExecuteFlow`.
 - Returns `WebAppFlowExecutionResponse` to the client.

## Runner Selection Policy (Recommended)

To avoid unpredictable selection and reduce UI friction, implement the following deterministic selection policy (server-side):

1. If `request.RunnerNames` is supplied by the client, resolve to the owner's online runners and use them (fail if none match).
2. Else if the app has `PreferredRunnerId` set and that runner is online and available, use it.
3. Else if the app owner (user) has a `DefaultRunnerId` set and that runner is online, use it.
4. Else choose a "best" online runner using a policy:
 - Prefer by tags/affinity if app requests a tag (optional), or
 - Pick least-loaded runner if you track load metrics, or
 - Round-robin deterministic selection among the owner's online runners.

If no runners are online, return400 with a clear message.

### Data Model Additions

- Add `PreferredRunnerId` (nullable GUID) to `WebApp` entity and `AppModel`.
- Add `DefaultRunnerId` (nullable GUID) to `UserSettings` or a `UserPreferences` table.

These fields let users and owners specify lightweight preferences without forcing per-app dedicated runners.

## API Changes & Contracts

- `GET api/apps/slug/{slug}` — returns `AppModel` or `404`. Must include `RequiresAuthentication` and `IsUserAuthenticated` (or return401) according to current design.
- `POST api/web-apps/{slug}/flows/{flowIdentifier}/execute`
 - Request body: `WebAppFlowExecutionRequest { Dictionary<string,string> Inputs; List<string> RunnerNames; }`
 - Response body: `WebAppFlowExecutionResponse` (Success, FlowId, Status, RunnerCount, ExecutedAt, Message, OutputVariables, Logs)

- `PUT api/apps/{id}` — support updating `PreferredRunnerId` when publishing or via app settings UI.
- `POST api/users/{id}/preferences/default-runner` — allow setting user default (or store in same profile endpoint).

## Implementation Details

### Server-side

- Implement a `RunnerSelector` helper in `RunnerService` or new `RunnerSelectionService`:
 - Method: `Task<IList<Guid>> SelectRunnersAsync(IEnumerable<RunnerDetails> onlineRunners, Guid? preferredRunnerId, Guid? userDefaultRunnerId, IEnumerable<string>? requestedNames)`
 - Returns list of runner IDs or empty if none matched.

- Use `UserImpersonationServiceFactory<T>` to create impersonated `FlowsService` for name resolution and execution.

- Keep `ExecuteFlow` code small by extracting helpers:
 - `ValidateAndLoadPublishedAppAsync` (delegates to `AppsService`)
 - `ResolveFlowIdAsync` (uses impersonated `FlowsService`)
 - `ApplyInputVariables` (pure helper returns missing keys)
 - `SelectRunnersAsync` (calls `RunnerSelector`) 

- Log structured events for:
 - App access (slug, appId, ownerId, viewer, authenticated)
 - Runner selection (reason: requested | preferred | user-default | fallback, runnerIds)
 - Flow execution result (flowId, success, runnerCount)

- Respect cancellation tokens for all async calls.

### UI (Minimal friction)

- In the `PublishAppDialog` used by `AppDesignerPage` add a small dropdown `Preferred runner (optional)` that lists the owner’s runners with status badges (online/offline).
- Add a checkbox: `Set as my default runner for new apps`.
 - If checked, the UI calls the user preferences API to store `DefaultRunnerId`.
- When opening `PublishAppDialog` pre-select the dropdown using the user default (via `AppsService` client call to get user settings) or existing `currentApp.PublicUrl` preferences.
- Do not require owners to set this; leave blank by default.

### UX notes

- Show small tooltips explaining the runner preference is optional and used only if the request does not specify a runner.
- Display runner status (online/offline), and show a warning if chosen runner is offline when publishing.

## Security & Traceability

- `WebAppFlowExecutionController` must remain `[AllowAnonymous]` but perform its own auth check against `webApp.RequiresAuthentication`.
- All executions occur under the app owner's permissions via `UserImpersonationServiceFactory<FlowsService>` to ensure the right data scope.
- Log viewer identity, owner identity, app slug, flow id and selected runners for each execution.
- Do not return stack traces or internal errors to clients; return generic error messages and log details server-side.
- Consider rate-limiting `ExecuteFlow` per app to avoid abuse.

## Backwards Compatibility

- The `ExecuteFlow` request shape does not change. Existing clients continue to work.
- New `PreferredRunnerId` and `DefaultRunnerId` are optional; absence means fallback selection policy applies.

## Migration Steps

1. Add database migrations:
 - Add `PreferredRunnerId` (nullable GUID) to `WebApps` table.
 - Add `DefaultRunnerId` (nullable GUID) to `UserPreferences` or `Users` table.
2. Update `AppModel` and DTOs to include `PreferredRunnerId`.
3. Update `AppsService` CRUD methods to persist the new field.
4. Add `RunnerSelector` helper and integrate into `WebAppFlowExecutionController` logic.
5. Update `PublishAppDialog` UI to allow preselecting the runner and saving user default.

## Testing Strategy

- Unit tests:
 - `RunnerSelector` logic (requested ? preferred ? user default ? fallback)
 - `ApplyInputVariables` helper (sets values, returns missing keys)
 - `FlowsService.ResolveFlowNameToId` behavior under impersonation (mocked)

- Integration tests:
 - `WebAppFlowExecutionController` end-to-end with mocked `RunnerService` and `FlowsService`.
 - Publish flow and execute via the `WebAppViewer`/iframe simulation.

- Manual tests:
 - Publish an app and set preferred runner (online/offline), observe behavior and fallbacks.
 - Verify logs contain selection reason and runner ids.

## Operational Considerations

- Monitor runner health and responsiveness.
- Track metrics: flow executions per app, failures due to unavailable runners, runner load distribution.
- Implement alerting on repeated runner failures or too many fallback events.

## Example Server-side Selection Pseudocode

```csharp
// server-side selection
if (request.RunnerNames?.Any() == true) {
 return ResolveRequestedRunnerIds(...);
}
if (app.PreferredRunnerId != null && IsRunnerOnline(app.PreferredRunnerId)) return [app.PreferredRunnerId];
if (user.DefaultRunnerId != null && IsRunnerOnline(user.DefaultRunnerId)) return [user.DefaultRunnerId];
return PickBestOnlineRunner(onlineRunners);
```

## Next Steps / Implementation Plan

1. Implement DB migration and add `PreferredRunnerId` to `WebApp` entity and `AppModel` (1-2 days).
2. Add `DefaultRunnerId` to user preferences and a tiny API to update it (1 day).
3. Implement `RunnerSelector` helper and unit tests (1-2 days).
4. Wire `RunnerSelector` into `WebAppFlowExecutionController` and add structured logging for selection reason (1 day).
5. Add UI changes to `PublishAppDialog` and preselect user default (1-2 days).
6. Run integration tests and minor fixes (1-2 days).

Estimated total:6–10 work days depending on QA and migration rollout.

---

Document produced by: GitHub Copilot (assistant) — Technical Architecture draft for Web Apps feature.
