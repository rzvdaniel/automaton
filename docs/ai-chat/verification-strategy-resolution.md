# Verification Strategy and Execution Policy Resolution

This document explains how app-generation verification strategy and execution policy are resolved, where they are configured, and where they are visible.

## Why This Exists

Verification strategy directly affects:
- runtime reliability (how often the AI verifies intermediate state)
- token/cost usage (extra checks increase API calls)
- build speed (more checks can be slower)

Execution policy directly affects:
- tool-call intensity (how chatty the agent is with tools)
- batching behavior (fewer larger edits vs many micro edits)
- redundant check suppression (reducing unnecessary read/verify loops)

Because Automaton supports personal and organization workflows, strategy resolution is policy-driven.

## Available Execution Policies

- `default`: correctness-first behavior, normal tool usage
- `lean-tools`: reduce tool churn with batching and fewer redundant loops

## Available Strategies

- `balanced`: default middle-ground behavior
- `continuous`: more frequent verification during implementation
- `final-gate`: lighter ongoing checks, stricter end-of-run validation

## Resolution Order

Effective strategy is resolved per app conversation using this precedence:

1. Global admin defaults from `AI Settings`
- `appVerificationDefaultStrategy`
- `appVerificationAllowUserOverride`

2. Organization policy (if app belongs to an organization and caller is an active org member)
- organization default strategy (if configured)
- organization `allow user override` toggle

3. User preference (only applied when override is allowed at the active scope)
- user setting: `ai.appVerificationStrategy`

4. Fallback
- `balanced`

Effective execution policy is resolved per app conversation using the same precedence:

1. Global admin defaults from `AI Settings`
- `appExecutionDefaultPolicy`
- `appExecutionAllowUserOverride`

2. Organization policy (if app belongs to an organization and caller is an active org member)
- organization default execution policy (if configured)
- organization `allow user override` toggle

3. User preference (only applied when override is allowed at the active scope)
- user setting: `ai.appExecutionPolicy`

4. Fallback
- `default`

## Non-Organization Behavior

For apps outside organizations (or when org policy cannot be applied), effective strategy is resolved from:
- global default + global `allow user override`
- user preference if allowed

Execution policy for non-organization apps is resolved the same way.

## Where It Is Configured

Global admin settings:
- Admin UI: `Admin -> AI Settings`
- API: `PUT /api/admin/ai-settings`

Organization settings:
- Organization UI: `Organization Settings -> AI Verification Policy`
- API: `PUT /api/organizations/{id}`

User settings:
- User UI: `Settings -> AI Preferences -> App Verification Strategy`
- User UI: `Settings -> AI Preferences -> App Execution Policy`
- API: `PUT /api/user-settings`

Organization settings:
- Organization UI: `Organization Settings -> AI Execution Policy`
- API: `PUT /api/organizations/{id}`

## Where It Is Visible

During active generation (SSE stream):
- `update` and `done` events include `effectiveVerificationStrategy`

In app UI:
- AI Assistant footer shows the effective strategy for the active session
- App Build Logs session cards show `Strategy: <name>` tag

In persisted chat history:
- A one-time system message marker is saved per session:
  - `Verification strategy: <strategy>`
- Session DTOs derive `effectiveVerificationStrategy` from this marker

## Notes

- Strategy names are normalized to known values (`continuous`, `balanced`, `final-gate`).
- Unknown values are treated as `balanced`.
- Execution policy names are normalized to known values (`default`, `lean-tools`).
- Unknown execution policy values are treated as `default`.
- Organization policy is only applied when the caller is an active member of the app's organization.
