# Verification Strategy Resolution

This document explains how the app-generation verification strategy is resolved, where it is configured, and where it is visible.

## Why This Exists

Verification strategy directly affects:
- runtime reliability (how often the AI verifies intermediate state)
- token/cost usage (extra checks increase API calls)
- build speed (more checks can be slower)

Because Automaton supports personal and organization workflows, strategy resolution is policy-driven.

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

## Non-Organization Behavior

For apps outside organizations (or when org policy cannot be applied), effective strategy is resolved from:
- global default + global `allow user override`
- user preference if allowed

## Where It Is Configured

Global admin settings:
- Admin UI: `Admin -> AI Settings`
- API: `PUT /api/admin/ai-settings`

Organization settings:
- Organization UI: `Organization Settings -> AI Verification Policy`
- API: `PUT /api/organizations/{id}`

User settings:
- User UI: `Settings -> AI Preferences -> App Verification Strategy`
- API: `PUT /api/user-settings`

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
- Organization policy is only applied when the caller is an active member of the app's organization.
