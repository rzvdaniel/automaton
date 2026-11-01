---
title: Pause Public Registration
order: 21
---

# Pause public registration

Pausing signup does not disable existing accounts, revoke keys, change plans, delete data or stop workloads. Hosted Runner provisioning is retired and is not controlled by signup or plan assignment.

The reconciled source baseline retains the public-registration gate and GitHub login fixes. Preparing or merging this source change does not change the running API configuration or database. Before deploying it, inspect the target database migration history for the removed migrations. The repository self-hosted roadmap records the exact migration IDs and release prerequisite.

After deployment and approval, set the existing API `AppConfig:UserSignUp` to `false`. For the production Compose installation, edit only this property in the mounted `/opt/automaton/config/appsettings.Production.json`:

```json
"AppConfig": {
  "UserSignUp": false
}
```

Keep other properties in the section. An environment override `AppConfig__UserSignUp` takes precedence; set it to `false` too if one is configured. Apply through the normal API blue-green deployment workflow: the flag is bound at process startup. Editing the file alone is not sufficient for an already running process. Studio reads the state from the API; no Studio build-time setting is needed.

Verify `GET /api/Account/RegistrationStatus` reports `enabled: false`, `POST /api/Account/RegisterUser` returns 403, and GitHub authentication for a missing account returns the pause message without creating a user. Studio `/register` hides both signup controls and displays:

> Public registration is temporarily paused while we prepare Automaton for wider access.

Password login, GitHub login for existing accounts, refresh, email confirmation and password recovery remain available. Existing administrator-controlled project/organization account creation and invitation completion remain available. Invitation completion requires a valid unconsumed token; it is not public self-registration. The API does not register Identity's default registration UI.

To reopen, set the same property to `true` and apply it through the normal API deployment workflow. No invitation system or data migration is required.

## Adding users while signup is paused

Administrators add people from **Admin → Users → Add user**:

- **Invite by email** creates the account without a password and emails a link to **Set your password** (template `AutomatonUserInvitation`). The link works once and expires after 24 hours; inviting the same address again sends a new link while the account has no password. Setting the password from the link confirms the email address. Configure **Admin → Installation → Outgoing email** first; if sending fails, the account is kept and Studio says so.
- **Create with password** creates the account with a password the administrator types or generates. A generated password is shown once and never stored in readable form; no email is sent. Use it for test accounts and people without email.
- Only system administrators can make the new user an administrator. Both actions are administrator-only, require a browser or JWT login (API keys are refused) and appear in **Admin → Audit Events** (`UserInvited`, `UserCreatedByAdministrator`) without passwords.

Project admins can add only people who already have an account. Adding an unknown email to a project is refused with a message to ask a server administrator for an invitation first; project invites never create accounts.

Admins add people while signup is paused; you can use plus addresses such as `you+test1@example.com` for separate test accounts that still reach your inbox.

## Existing-account execution review

Signup is not an execution safeguard. Review each account in Administration > Users for roles and assigned/effective plan, then inspect its runners, apps and pending execution operations. Unassigned accounts may inherit the default active plan. Plan assignment does not create a Hosted Runner or determine Machine Management Runner access.

Machine Management Runner assignment and execution depend on the existing app, workspace, and Runner authorization checks. The former Infrastructure and Hosted Runner administration screens are retired. Existing records remain in the database as history.

Before wider access, independently validate live image provenance, tenant authorization on execution and secret grants, CPU/memory/PID limits, capability dropping/no-new-privileges, filesystem and Docker-socket isolation, writable-layer/durable-storage limits, outbound network restrictions, cancellation/timeouts and retry behavior. Source inspection and unit tests do not establish live enforcement. Do not revoke permissions, change subscriptions or stop workloads as part of this review.

## GitHub login build configuration

Studio uses `VITE_GITHUB_AUTH_API_BASE_URL` when explicitly configured, otherwise it uses `VITE_API_BASE_URL`. Leave the GitHub override empty when both flows use the same API. These are build-time values: rebuilding Studio is required after changing them. The Docker image leaves the GitHub override empty so production Compose's configured API domain is used instead of a development localhost address.

When GitHub returns the exact registration-pause message for a missing account, Studio displays that message and offers **Back to Sign In**, without an immediate GitHub retry prompt. Other OAuth errors retain generic wording. Existing GitHub users continue through the normal sign-in flow.
