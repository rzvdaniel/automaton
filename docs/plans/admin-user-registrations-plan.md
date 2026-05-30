# Admin User Registrations Plan

**Created:** May 30, 2026  
**Status:** Proposed  
**Target:** Automaton Studio Vue + ASP.NET Core backend

---

## Overview

Automaton already has a protected admin surface inside Studio and admin-only user endpoints in the backend. The gap is not a missing admin application. The gap is missing visibility into who registered, how they registered, whether they confirmed email, and what they have done since registering.

**Recommendation:** Keep administration inside Automaton Studio for now. Expand the existing `/admin` area with user-registration operations instead of creating a separate admin website.

---

## Decision Summary

### Keep admin inside Studio now

Why:

- Admin routes already exist in Studio under `/admin`
- The sidebar already hides admin navigation for non-admin users
- The backend already has admin-only user endpoints
- A separate admin website would add deployment, auth, routing, and maintenance complexity without solving the current visibility problem

### Do not create a separate admin site yet

Create a separate admin app only if one of these becomes true:

- internal operations need a different deploy cadence than Studio
- admin access must be isolated behind a different network boundary
- the admin UI becomes large enough to behave like an internal product owned separately
- support/ops users need a different authentication flow than product admins

---

## Current State

### What already exists

- Protected admin route tree in Studio
- Admin-only backend user query endpoints
- Centralized registration endpoint in `AccountController`
- Existing email pipeline for transactional email
- Existing server logs page in admin

### What is missing

- no dedicated admin users or registrations page
- no registration-specific internal notification email
- no normalized registration telemetry for admin reporting
- no last-login timestamp on the user model
- no explicit registration provider field for easy filtering
- no lightweight account lifecycle audit feed

---

## Goals

- Give admins immediate visibility into new registrations
- Make user operations a first-class area inside Studio admin
- Support basic user support flows without overbuilding
- Reuse the existing auth, email, and admin route infrastructure

## Non-Goals

- separate admin website
- full analytics platform
- heavy BI dashboards
- impersonation tooling
- broad manual editing of all user fields

---

## Target Admin Surface

### Routes

Add these routes inside the existing admin area:

- `/admin/users`
- `/admin/users/:id`
- `/admin/registrations`

### Recommended UX

Start simple:

- `Users` page for operational management
- `Registrations` page for recent signups and funnel visibility
- User detail page or side drawer for support actions

If speed matters more than purity, Phase 1 can ship as a single `/admin/users` page with registration-focused filters instead of two separate pages.

---

## Data Needed For Visibility

The current user query shape is too small for an admin operations screen. Extend the backend so the admin UI can show the following fields directly:

| Field | Purpose |
|------|---------|
| `RegisteredAtUtc` | sort/filter by signup date |
| `LastLoginAtUtc` | show whether the user is active |
| `RegistrationProvider` | filter password vs GitHub |
| `EmailConfirmed` | identify unconfirmed users |
| `LockoutEnd` / locked state | identify locked users |
| app count | identify engaged users |
| runner count | identify technical users |
| plan name / subscription status | identify paid or test users |

### Provider model

Do not scatter provider strings. Use a constants class, for example:

```csharp
public static class RegistrationProviders
{
    public const string Password = "password";
    public const string GitHub = "github";
    public const string Invitation = "invitation";
    public const string AdminCreated = "adminCreated";
}
```

This follows the existing repo rule to avoid magic strings for well-known values.

---

## Backend Plan

### Controller shape

Use a dedicated admin controller instead of extending the generic `UserController`.

Suggested file:

- `src/Automaton.Studio.Server/Controllers/Admin/AdminUsersController.cs`

Suggested controller shape:

```csharp
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Administrators")]
public class AdminUsersController : ControllerBase
{
}
```

Why this is the right boundary:

- keeps admin-only operations under the existing `/api/admin/*` backend namespace
- matches the existing admin controller pattern already used for logs, hosted runners, plans, and AI providers
- avoids mixing self-service user endpoints with admin operations
- gives the frontend a clean backend surface for `/admin/users` and `/admin/registrations`

Note:

- the canonical backend admin authorization policy is `Administrators`
- avoid introducing a second policy name for the new controller
- the existing generic `UserController` can remain temporarily for compatibility, but the target direction should be to move admin operations behind `AdminUsersController`

### Phase 1: Registration telemetry and notification

#### 1.1 Extend `ApplicationUser`

Add fields to support admin visibility:

- `DateTime RegisteredAtUtc`
- `DateTime? LastLoginAtUtc`
- `string RegistrationProvider`

Notes:

- Set `RegisteredAtUtc` when the account is created
- Set `RegistrationProvider` in each creation path
- Update `LastLoginAtUtc` on successful login and successful external sign-in

#### 1.2 Update registration paths

Set provider consistently in all entry points:

- password signup via `AccountController.RegisterUser`
- GitHub OAuth account creation flow
- invitation acceptance flow when it creates a new account
- install/bootstrap admin creation if it should appear in admin history

#### 1.3 Add internal registration notification email

After successful account creation, send a best-effort internal notification to configured admin recipients.

Suggested subject:

```text
New Automaton registration
```

Suggested body:

```text
A new user registered in Automaton.

Email: user@example.com
Username: johndoe
Name: John Doe
Provider: github
Email confirmed: no
Registered at: 2026-05-30 14:22 UTC
```

Implementation notes:

- use existing `IEmailService`
- do not fail registration if the notification email fails
- make recipients configuration-based, for example `EmailConfig.AdminNotificationRecipients`
- keep this separate from the user-facing confirmation email

#### 1.4 Add migration

Create an EF Core migration for the new user columns.

Backfill strategy:

- `RegisteredAtUtc`: set to migration time or infer from earliest related identity/activity data when practical
- `RegistrationProvider`: default to `password` unless external login rows clearly indicate otherwise
- `LastLoginAtUtc`: leave null for historical users if unknown

### Phase 2: Admin users query surface

Create a dedicated admin query surface behind `AdminUsersController` that returns a paged user operations DTO.

Suggested response shape:

```csharp
public class AdminUserListItemDto
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string PublicUsername { get; set; }
    public bool EmailConfirmed { get; set; }
    public DateTime RegisteredAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public string RegistrationProvider { get; set; }
    public bool IsLocked { get; set; }
    public int AppCount { get; set; }
    public int RunnerCount { get; set; }
    public string[] Roles { get; set; } = [];
}
```

Suggested filters:

- search by email, name, username
- registered today / last 7 days / custom range
- email confirmed yes/no
- provider
- locked only
- has apps
- has runners

Suggested endpoint:

- `GET /api/admin/users`

Optional adjacent endpoints:

- `GET /api/admin/users/{id}`
- `POST /api/admin/users/{id}/resend-confirmation`
- `POST /api/admin/users/{id}/lock`
- `POST /api/admin/users/{id}/unlock`

Implementation note:

- if the current `UserController` admin endpoints are still used elsewhere, keep them only as a short-term compatibility layer and migrate the frontend admin UI to `AdminUsersController`

### Phase 3: Account lifecycle audit

Introduce a lightweight audit stream for user lifecycle events.

Suggested events:

- user registered
- email confirmed
- login succeeded
- user locked
- user unlocked
- confirmation resent

This does not need a full security audit platform in the first slice. A small append-only table or reuse of an existing recent-activity style pattern is enough.

---

## Migration Checklist

Use this checklist to migrate the current backend from mixed admin patterns to a consistent admin surface.

### A. Fix broken admin authorization first

- [ ] Replace the invalid `Policy = "Admin"` usage in the current user-management endpoints.
- [ ] Standardize global admin endpoints on `Policy = "Administrators"`.
- [ ] Confirm there is no runtime dependency on an undefined `Admin` policy.

Why this is first:

- current `UserController` admin actions are protected by a policy name that is not defined in `Program.cs`
- migration should not preserve a broken authorization boundary

### B. Introduce dedicated admin users backend

- [ ] Create `src/Automaton.Studio.Server/Controllers/Admin/AdminUsersController.cs`.
- [ ] Add `GET /api/admin/users` for paged/filterable user operations.
- [ ] Add `GET /api/admin/users/{id}` for admin user detail.
- [ ] Add admin support actions only if needed for Phase 1:
- [ ] `POST /api/admin/users/{id}/resend-confirmation`
- [ ] `POST /api/admin/users/{id}/lock`
- [ ] `POST /api/admin/users/{id}/unlock`
- [ ] Keep route and auth shape aligned with existing admin controllers.

Decision rule:

- admin user-management belongs in `AdminUsersController`
- self-service account flows remain in `AccountController`

### C. Migrate generic user admin endpoints

- [ ] Inventory which frontend or backend callers still use `UserController` admin actions.
- [ ] Move admin-only user list/detail/role-management endpoints behind `AdminUsersController`.
- [ ] Update callers to use `/api/admin/users/...`.
- [ ] Leave `GetUserProfile` in `UserController` because it is not a global admin operation.
- [ ] Remove or deprecate old admin-facing `UserController` actions after callers are migrated.

Boundary rule:

- keep user self-service and API-key compatible profile endpoints out of `Controllers/Admin`
- move platform-wide user-management endpoints into `Controllers/Admin`

### D. Migrate global admin settings surface

- [ ] Create `src/Automaton.Studio.Server/Controllers/Admin/AdminSettingsController.cs`.
- [ ] Move app deploy rules endpoints from `SettingsController` to `/api/admin/settings/...`.
- [ ] Update the admin frontend service to call the new route.
- [ ] Remove or deprecate the old `SettingsController` admin-only endpoints once callers are migrated.

Why this should move:

- the feature is already presented inside the Studio admin area
- the controller is global-admin-only and fits the `/api/admin/*` namespace better than the generic settings surface

### E. Decide what to do with app moderation actions

- [ ] Review `AppsController.SetSampleStatus` and confirm whether it is platform moderation or app-owner workflow.
- [ ] If it is platform moderation, move it into an admin controller such as `AdminMarketplaceAppsController` or `AdminAppsController`.
- [ ] If it remains in `AppsController`, change its authorization to the canonical `Administrators` policy and document why it stays outside `Controllers/Admin`.

Decision rule:

- global curation and moderation actions belong under `/api/admin/*`
- resource-owner workflows stay with the resource controller

### F. Add registration visibility data

- [ ] Add `RegisteredAtUtc`, `LastLoginAtUtc`, and `RegistrationProvider` to `ApplicationUser`.
- [ ] Create the EF Core migration.
- [ ] Backfill historical users with safe defaults.
- [ ] Set these fields consistently in password signup, GitHub signup, invitation-created accounts, and bootstrap/admin-created accounts.

### G. Add internal registration notifications

- [ ] Add configuration for internal registration notification recipients.
- [ ] Send a best-effort internal email when a new user is created.
- [ ] Keep notification failures non-fatal for registration.
- [ ] Separate internal notification content from the existing user-facing email confirmation flow.

### H. Build the admin frontend on the new backend

- [ ] Add `AdminUsersView` and optional `AdminUserDetailView`.
- [ ] Add `adminUsersService.ts` targeting `/api/admin/users`.
- [ ] Add filters for recency, provider, confirmation state, locked state, apps, and runners.
- [ ] Add admin route entries under `/admin/users` and optionally `/admin/registrations`.
- [ ] Use `/admin/users` as the first slice; add `/admin/registrations` only if a dedicated view proves necessary.

### I. Remove inconsistency after migration

- [ ] Eliminate remaining uses of undefined or ad hoc admin policy names for global admin endpoints.
- [ ] Prefer `Policy = "Administrators"` over inline role strings for global admin controllers.
- [ ] Keep the backend admin route family coherent under `/api/admin/*`.
- [ ] Update related docs if route names change.

### J. Validation checklist

- [ ] Verify admin routes return `403` or `401` correctly for non-admin users.
- [ ] Verify existing admin users can access all migrated routes.
- [ ] Verify frontend admin pages are switched to the new endpoints.
- [ ] Verify old routes are removed only after callers are migrated.
- [ ] Run backend build and relevant tests after migration.

### Suggested implementation order

1. Fix `UserController` authorization mismatch.
2. Create `AdminUsersController` and move admin user list/detail there.
3. Move admin settings endpoints to `AdminSettingsController`.
4. Decide and clean up `AppsController.SetSampleStatus`.
5. Add registration telemetry fields and migration.
6. Add internal registration notification email.
7. Build `/admin/users` in Studio.
8. Remove deprecated endpoints and normalize remaining admin auth usage.

---

## Frontend Plan

### Phase 1: Admin Users view

Add a new admin feature view under the existing admin module.

Suggested files:

```text
src/automaton-studio-vue/src/features/admin/views/AdminUsersView.vue
src/automaton-studio-vue/src/features/admin/views/AdminUserDetailView.vue
src/automaton-studio-vue/src/features/admin/services/adminUsersService.ts
src/automaton-studio-vue/src/features/admin/types/admin-users.types.ts
```

### Page contents

Table columns:

- email
- name
- public username
- provider
- email confirmed
- registered at
- last login
- apps
- runners
- status

Actions:

- open user detail
- resend confirmation
- lock/unlock account

### Phase 2: Registrations-focused view

If needed after the users page lands, add `/admin/registrations` as a filtered operational view focused on:

- newest registrations first
- confirmation status
- provider mix
- quick anomaly detection

This page can reuse the same API as `/admin/users` and just apply a different default filter and presentation.

---

## Delivery Order

1. Add backend user telemetry fields and migration.
2. Set telemetry in all account-creation and login paths.
3. Add internal registration notification email.
4. Expose paged admin users API.
5. Build `/admin/users` in Studio admin.
6. Add user detail and small support actions.
7. Add `/admin/registrations` only if the users page proves too broad.
8. Add lightweight account lifecycle audit feed.

---

## Why This Is Better Than A Separate Admin App Right Now

- solves the actual visibility problem first
- reuses existing role-based protections
- keeps all admin capability under one auth/session model
- avoids duplicating navigation, API client setup, and deploy pipelines
- leaves the door open to split later if the admin surface genuinely becomes its own product

---

## Success Criteria

- admins receive a best-effort email when a user registers
- admins can view recent users in Studio without database access
- admins can filter by provider, confirmation state, and recency
- admins can tell which users are active versus abandoned
- the implementation adds minimal complexity to the main product surface

---

## Open Questions

1. Do you want `/admin/registrations` as a separate page immediately, or should Phase 1 ship only `/admin/users` with registration filters?
2. Should admin notification emails go to one fixed address, multiple recipients, or a future notification channel abstraction?
3. Do you want GitHub registrations to count as `email confirmed` immediately when GitHub returns a verified email?
4. Should invitation-created users be tracked as `invitation` provider even if they later sign in with password?
5. Do you want `LastLoginAtUtc` updated only for interactive Studio logins, or also for API-key and app-user execution scenarios?
